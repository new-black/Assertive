using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  /// <summary>
  /// Decides whether an assertion call site is whitelisted for interception, and if so
  /// produces everything emission needs. The whitelist is deliberately conservative: any
  /// doubt means "don't intercept", which leaves the call site on the degraded delegate
  /// path (source text only, no decomposition).
  ///
  /// Recognized forms (each with outer !-negations): ==/!= comparisons (routed to the
  /// equality, null-check or length pattern), &lt;/&lt;=/&gt;/&gt;= comparisons (comparison or
  /// length pattern), .Equals(x), ReferenceEquals(x, y), `is T` / `is object`, .HasValue,
  /// and bare bool members. Each operand is compiled independently: typed C# when every
  /// name involved is nameable from the generated file, otherwise a reflective fallback
  /// that reads the closure and resolves members at runtime (private nested types, private
  /// members on `this`).
  /// </summary>
  internal static partial class CallSiteAnalyzer
  {
    public static InterceptedCall? Analyze(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
      {
        return null;
      }

      var isAssertiveContainer = method.ContainingType is { Name: "Assert" or "DSL", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } };

      if (method.Name == "Throws" && isAssertiveContainer)
      {
        return AnalyzeThrows(ctx, invocation, method, ct);
      }

      // First parameter must be the assertion delegate; this also excludes the DSL's
      // snapshot overload (first parameter is `object`).
      if (method.Parameters.Length == 0
          || method.Parameters[0].Type is not INamedTypeSymbol { Name: "Func", Arity: 1 } assertionDelegate)
      {
        return null;
      }

      // Func<bool> is the synchronous assertion; Func<Task<bool>> the async overloads,
      // intercepted with a Task-returning interceptor whose render callback is async
      // (awaited operands are re-evaluated inside it).
      var isAsync = IsTaskOfBool(assertionDelegate.TypeArguments[0]);

      if (!isAsync && assertionDelegate.TypeArguments[0].SpecialType != SpecialType.System_Boolean)
      {
        return null;
      }

      var isAssertThat = method is { Name: "That", ContainingType: { Name: "Assert", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } } };
      var isDslAssert = method is { Name: "Assert", ContainingType: { Name: "DSL", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } } };

      ThatOverload? overload = null;
      WrapperModel? wrapper = null;

      if (isAssertThat || isDslAssert)
      {
        overload = ClassifyOverload(method);
      }
      else if (!isAsync && HasAssertionWrapperAttribute(method))
      {
        // AssertionHandle carries a synchronous condition; async wrappers stay degraded.
        wrapper = AnalyzeWrapper(method, ctx.SemanticModel.Compilation);
      }

      if (overload == null && wrapper == null)
      {
        return null;
      }

      if (invocation.ArgumentList.Arguments.Count == 0
          || invocation.ArgumentList.Arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } body, ParameterList.Parameters.Count: 0 })
      {
        return null;
      }

      // Arguments must be positional for the interceptor's straight forwarding to be faithful.
      if (invocation.ArgumentList.Arguments.Any(a => a.NameColon != null))
      {
        return null;
      }

      // Recognize the supported forms, peeling outer !-negations.
      var outerNegated = false;
      var core = StripParens(body);

      while (core is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
      {
        outerNegated = !outerNegated;
        core = StripParens(negation.Operand);
      }

      var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);

      if (location == null)
      {
        return null;
      }

      var call = new InterceptedCall
      {
        LocationVersion = location.Version,
        LocationData = location.Data,
        DisplayLocation = location.GetDisplayLocation(),
        FilePath = invocation.SyntaxTree.FilePath,
        Overload = overload ?? ThatOverload.MessageContext,
        Wrapper = wrapper,
        IsAsync = isAsync,
        BodySource = body.ToString(),
        Negated = outerNegated,
      };

      // The context argument's source text labels the CONTEXT section; without the
      // CallerArgumentExpression parameters it is reconstructed here from the call site.
      if (overload != null)
      {
        var contextArgIndex = overload == ThatOverload.Context ? 1 : 2;
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count > contextArgIndex
            && !arguments[contextArgIndex].Expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
          call.ContextSource = arguments[contextArgIndex].Expression.ToString();
        }
      }

      var compiler = new OperandCompiler(ctx.SemanticModel, body, call, ct);

      if (!ClassifyForm(ctx, core, outerNegated, call, compiler, ct))
      {
        // Not a whitelisted decomposable form: intercept it opaquely anyway. The false
        // path reports source text (same as the degraded path) plus readable locals; the
        // exception path gets full cause attribution via the recorded steps.
        call.Kind = InterceptionKind.Opaque;
        call.Negated = false;
        call.LeftSource = "";
        call.RightSource = "";
        call.LeftDisplay = "";
        call.RightDisplay = "";

        // Best-effort compile purely to register captured locals for the LOCALS section.
        _ = compiler.Compile(body);
      }

      call.ExceptionStepsSource = new ExceptionStepWalker(ctx.SemanticModel, compiler, body, ct).CollectSource(body);
      call.CustomProbeSource = BuildCustomProbe(ctx, core, outerNegated, compiler, ct);

      return call;
    }

    /// <summary>
    /// Analyzes an Assert.Throws / DSL.Throws call site with an exception-predicate lambda:
    /// the predicate body is classified as a sub-assertion with the parameter bound to the
    /// thrown exception, so a failing predicate gets a decomposed report instead of just
    /// source text. Call sites without a predicate lambda stay on the normal path.
    /// </summary>
    private static InterceptedCall? AnalyzeThrows(GeneratorSyntaxContext ctx, InvocationExpressionSyntax invocation, IMethodSymbol method, CancellationToken ct)
    {
      if (invocation.ArgumentList.Arguments.Count < 2
          || invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
          || invocation.ArgumentList.Arguments[1].Expression is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax predicateBody } predicateLambda)
      {
        return null;
      }

      // Only the delegate-based overloads (Action / Func<object?> / Func<Task>).
      ThrowsActionKind? actionKind = method.Parameters[0].Type switch
      {
        INamedTypeSymbol { Name: "Action", Arity: 0 } => ThrowsActionKind.Action,
        INamedTypeSymbol { Name: "Func", Arity: 1 } func when func.TypeArguments[0].Name == "Task" => ThrowsActionKind.FuncTask,
        INamedTypeSymbol { Name: "Func", Arity: 1 } => ThrowsActionKind.FuncObject,
        _ => null,
      };

      if (actionKind == null)
      {
        return null;
      }

      // The exception type appears in the interceptor signature, so it must be nameable.
      var exceptionType = method.IsGenericMethod ? method.TypeArguments[0] : null;
      var exceptionTypeForBinding = exceptionType
        ?? ctx.SemanticModel.Compilation.GetTypeByMetadataName("System.Exception")!;

      if (exceptionType != null && !IsUsableType(exceptionType, ctx.SemanticModel.Compilation))
      {
        return null;
      }

      if (ctx.SemanticModel.GetDeclaredSymbol(predicateLambda.Parameter, ct) is not { } parameterSymbol)
      {
        return null;
      }

      var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);

      if (location == null)
      {
        return null;
      }

      var call = new InterceptedCall
      {
        LocationVersion = location.Version,
        LocationData = location.Data,
        DisplayLocation = location.GetDisplayLocation(),
        FilePath = invocation.SyntaxTree.FilePath,
        Kind = InterceptionKind.ThrowsPredicate,
        ThrowsActionKind = actionKind.Value,
        ThrowsExceptionTypeFqn = exceptionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        BodySource = predicateLambda.ToString(),
      };

      var exceptionTypeFqn = (exceptionType ?? exceptionTypeForBinding).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      var compiler = new OperandCompiler(ctx.SemanticModel, predicateLambda, call, ct);

      var bindings = new Dictionary<string, LambdaBinding>
      {
        [parameterSymbol.Name] = new LambdaBinding($"(({exceptionTypeFqn})__i)", "__i"),
      };

      var subNegated = false;
      var subCore = StripParens(predicateBody);

      while (subCore is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
      {
        subNegated = !subNegated;
        subCore = StripParens(negation.Operand);
      }

      var subCall = new InterceptedCall { Negated = subNegated };

      if (!ClassifyForm(ctx, subCore, subNegated, subCall, compiler, ct, bindings))
      {
        // No decomposable predicate body: nothing to gain from interception.
        return null;
      }

      call.AllSubCall = subCall;

      return call;
    }

    /// <summary>
    /// Builds the CustomPatternProbe initializer for bodies whose (negation-stripped) root
    /// is a method call or property access — the shapes runtime-registered custom patterns
    /// can match. Null for any other shape.
    /// </summary>
    private static string? BuildCustomProbe(GeneratorSyntaxContext ctx, ExpressionSyntax core, bool negated,
      OperandCompiler compiler, CancellationToken ct)
    {
      var parts = new List<string> { $"Negated = {(negated ? "true" : "false")}" };

      switch (core)
      {
        case InvocationExpressionSyntax invocation
          when ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method:
        {
          var access = invocation.Expression is MemberAccessExpressionSyntax ma && ma.IsKind(SyntaxKind.SimpleMemberAccessExpression)
            ? ma
            : null;

          ExpressionSyntax? instance = null;
          var argExpressions = invocation.ArgumentList.Arguments.Select(a => a.Expression).ToList();
          int parameterCount;

          if (method.ReducedFrom != null)
          {
            if (access == null)
            {
              return null;
            }

            instance = access.Expression;
            parameterCount = method.Parameters.Length;
          }
          else if (method.IsExtensionMethod)
          {
            // Extension method invoked in static form: the first argument is the instance.
            if (argExpressions.Count == 0)
            {
              return null;
            }

            instance = argExpressions[0];
            argExpressions.RemoveAt(0);
            parameterCount = method.Parameters.Length - 1;
          }
          else
          {
            if (!method.IsStatic && access != null)
            {
              instance = access.Expression;
            }

            parameterCount = method.Parameters.Length;
          }

          parts.Add("IsMethodCall = true");
          parts.Add($"MemberName = {Quote(method.Name)}");
          parts.Add($"ParameterCount = {parameterCount}");
          parts.Add($"IsExtension = {(method.IsExtensionMethod ? "true" : "false")}");

          if (compiler.TypeAccessor(method.ContainingType) is { } declaringType)
          {
            parts.Add($"DeclaringType = {declaringType}");
          }

          AddProbeInstance(parts, ctx, instance, compiler, ct);

          if (argExpressions.Count > 0)
          {
            var sources = argExpressions.Select(a => Quote(a.ToString()));
            var evaluators = argExpressions.Select(a =>
              a is AnonymousFunctionExpressionSyntax || ContainsAwait(a) ? "null"
                : compiler.Compile(a) is { } compiled ? $"() => (object)({compiled})" : "null").ToList();
            var types = argExpressions.Select(a =>
              ctx.SemanticModel.GetTypeInfo(a, ct).Type is { } argType ? compiler.TypeAccessor(argType) ?? "null" : "null").ToList();

            // The runtime null-guards the value and type arrays: emit only non-default ones.
            parts.Add($"ArgSources = [{string.Join(", ", sources)}]");

            if (evaluators.Any(e => e != "null"))
            {
              parts.Add($"Args = [{string.Join(", ", evaluators)}]");
            }

            if (types.Any(t => t != "null"))
            {
              parts.Add($"ArgStaticTypes = [{string.Join(", ", types)}]");
            }
          }

          break;
        }

        case MemberAccessExpressionSyntax member
          when member.IsKind(SyntaxKind.SimpleMemberAccessExpression)
               && ctx.SemanticModel.GetSymbolInfo(member, ct).Symbol is IPropertySymbol { IsStatic: false } property:
        {
          parts.Add("IsMethodCall = false");
          parts.Add($"MemberName = {Quote(property.Name)}");

          if (compiler.TypeAccessor(property.ContainingType) is { } declaringType)
          {
            parts.Add($"DeclaringType = {declaringType}");
          }

          AddProbeInstance(parts, ctx, member.Expression, compiler, ct);

          if (!ContainsAwait(member) && compiler.Compile(member) is { } value)
          {
            parts.Add($"Value = () => (object)({value})");
          }

          break;
        }

        default:
          return null;
      }

      return $"new __CP {{ {string.Join(", ", parts)} }}";
    }

    private static void AddProbeInstance(List<string> parts, GeneratorSyntaxContext ctx, ExpressionSyntax? instance,
      OperandCompiler compiler, CancellationToken ct)
    {
      if (instance == null)
      {
        return;
      }

      parts.Add($"InstanceSource = {Quote(instance.ToString())}");

      if (ctx.SemanticModel.GetTypeInfo(instance, ct).Type is { } instanceType
          && compiler.TypeAccessor(instanceType) is { } instanceTypeAccessor)
      {
        parts.Add($"InstanceStaticType = {instanceTypeAccessor}");
      }

      if (!ContainsAwait(instance) && compiler.Compile(instance) is { } compiledInstance)
      {
        parts.Add($"Instance = () => (object)({compiledInstance})");
      }
    }

    /// <summary>Logical operators that split into separate assertions (&/| only over bools).</summary>
    private static bool IsSplittableLogical(BinaryExpressionSyntax binary, GeneratorSyntaxContext ctx, CancellationToken ct)
    {
      switch (binary.Kind())
      {
        case SyntaxKind.LogicalAndExpression:
        case SyntaxKind.LogicalOrExpression:
          return true;

        case SyntaxKind.BitwiseAndExpression:
        case SyntaxKind.BitwiseOrExpression:
          return ctx.SemanticModel.GetTypeInfo(binary, ct).Type?.SpecialType == SpecialType.System_Boolean;

        default:
          return false;
      }
    }

    /// <summary>Syntax-level pure &&-only check (no ||, &, | anywhere in the tree).</summary>
    private static bool IsPureAndAlsoSyntax(ExpressionSyntax expression)
    {
      expression = StripParens(expression);

      if (expression is not BinaryExpressionSyntax binary)
      {
        return true;
      }

      var kind = binary.Kind();

      if (kind != SyntaxKind.LogicalAndExpression)
      {
        // Non-logical binary (==, !=, +, >, etc.) is a leaf, not a chain operator — OK.
        // Logical || / & / | make the chain mixed, which breaks pattern-var scoping.
        return kind is not (SyntaxKind.LogicalOrExpression
          or SyntaxKind.BitwiseAndExpression
          or SyntaxKind.BitwiseOrExpression);
      }

      return IsPureAndAlsoSyntax(binary.Left) && IsPureAndAlsoSyntax(binary.Right);
    }

    /// <summary>
    /// Builds the part tree of a logically-composed assertion. Each leaf carries a pasted
    /// re-evaluation of its source (exact semantics), its classified decomposition (when the
    /// leaf is a whitelisted form), a custom-pattern probe, its exception steps, and the
    /// captured locals it introduced. Null when any leaf cannot be re-evaluated.
    ///
    /// patternBindings accumulates pattern-variable bindings across &&-chain leaves
    /// (left-to-right), enabling cross-conjunct pattern var use (obj is T u &amp;&amp; u.Prop == x).
    /// </summary>
    private static SplitPart? BuildSplitPart(GeneratorSyntaxContext ctx, ExpressionSyntax expression,
      InterceptedCall call, OperandCompiler compiler, CancellationToken ct,
      Dictionary<string, LambdaBinding>? patternBindings = null)
    {
      expression = StripParens(expression);

      if (expression is BinaryExpressionSyntax binary && IsSplittableLogical(binary, ctx, ct))
      {
        // For &&-only chains, the same patternBindings dict flows left→right so pattern vars
        // declared in left subtrees are visible in right subtrees. For other operators (||, &,
        // |) pattern vars don't cross branches (C# semantics), so bindings are withheld.
        var isAndAlso = binary.IsKind(SyntaxKind.LogicalAndExpression);
        var left = BuildSplitPart(ctx, binary.Left, call, compiler, ct, isAndAlso ? patternBindings : null);
        var right = left != null ? BuildSplitPart(ctx, binary.Right, call, compiler, ct, isAndAlso ? patternBindings : null) : null;

        if (right == null)
        {
          return null;
        }

        var kind = binary.Kind() switch
        {
          SyntaxKind.LogicalAndExpression => "AndAlso",
          SyntaxKind.LogicalOrExpression => "OrElse",
          SyntaxKind.BitwiseAndExpression => "And",
          _ => "Or",
        };

        return new SplitPart { Kind = kind, Left = left, Right = right };
      }

      // Expand and-patterns (e.g., n is >= 0 and <= 100) into an AndAlso sub-tree.
      // No patternBindings guard: and-arms are simple sub-patterns with no pre-declared vars,
      // so they work correctly in both &&-only and mixed-operator chains.
      if (expression is IsPatternExpressionSyntax { Expression: var andSubject, Pattern: BinaryPatternSyntax { } andBinPat }
          && andBinPat.IsKind(SyntaxKind.AndPattern))
      {
        return BuildAndPatternTree(ctx, andSubject, andBinPat, call, compiler, ct, patternBindings);
      }

      // Expand property patterns (obj is User { Name: "Bob" } or obj is { Name: "Bob" }) into
      // optional type-check + property sub-leaves.
      // Guarded to &&-only chains (patternBindings != null): typed property leaves reference a
      // pre-declared cast variable that BuildFlagsChain would not emit.
      if (patternBindings != null
          && expression is IsPatternExpressionSyntax { Expression: var ppSubject, Pattern: RecursivePatternSyntax { PropertyPatternClause: not null } ppPat })
      {
        return BuildPropertyPatternSubTree(ctx, ppSubject, ppPat, call, compiler, ct, patternBindings);
      }

      // Expand list patterns (arr is [1, _, > 0]) into a length check + element sub-leaves.
      // Var/declaration bindings (arr is [var first, ..]) are registered in patternBindings so
      // subsequent conjuncts can reference them; each also gets a capture leaf that assigns the
      // element to a pre-declared outer variable via a `is var` pattern (which always matches).
      if (expression is IsPatternExpressionSyntax { Expression: var lpSubject, Pattern: ListPatternSyntax lpPat })
      {
        return BuildListPatternSubTree(ctx, lpSubject, lpPat, call, compiler, ct, patternBindings);
      }

      // Leaf: locals registered while compiling this leaf belong to its report.
      var localsBefore = call.CapturedLocals.Count;

      // Detect a pattern-variable declaration in this leaf: `obj is T u` or `obj is T { } u`.
      // When the chain is &&-only (patternBindings non-null) and the type is nameable, set up:
      //   - A pre-declared outer nullable var (PreDeclName) accessible outside the try block.
      //   - A temp pattern var name (TmpName) used inside the condition to avoid CS0136.
      //   - A LambdaBinding entry so subsequent leaves see the pre-declared var in their code.
      // The condition source is compiled with designationRenames so it references TmpName.
      Dictionary<string, string>? designationRenames = null;
      var patternVarDecls = new List<(string TypeFqn, string PreDeclName, string TmpName)>();

      if (patternBindings != null
          && expression is IsPatternExpressionSyntax
          {
            Pattern: DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax pvDesig }
          }
          && ctx.SemanticModel.GetDeclaredSymbol(pvDesig, ct) is ILocalSymbol pvSymbol
          && IsUsableType(pvSymbol.Type, ctx.SemanticModel.Compilation))
      {
        var typeFqn = pvSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var origName = pvDesig.Identifier.ValueText;
        var pvIndex = patternBindings.Count;
        var preDeclName = $"__pv{pvIndex}_{origName}";
        var tmpName = $"__pvtmp{pvIndex}_{origName}";

        designationRenames = new Dictionary<string, string> { [origName] = tmpName };

        // The TypedReplacement cast peels the nullable and casts to the declared type.
        patternBindings[origName] = new LambdaBinding($"(({typeFqn}){preDeclName}!)", preDeclName);
        patternVarDecls.Add((typeFqn, preDeclName, tmpName));
      }

      // Detect out-var declarations in method call arguments (e.g., dict.TryGetValue(k, out var v)).
      // Uses the same pre-declaration + designation-rename mechanism as is-pattern vars.
      // Only in &&-only chains (patternBindings != null) so BuildAndChain emits the pre-declarations.
      if (patternBindings != null && expression is InvocationExpressionSyntax outVarCall)
      {
        foreach (var arg in outVarCall.ArgumentList.Arguments)
        {
          if (arg.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
              && arg.Expression is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax outDesig }
              && ctx.SemanticModel.GetDeclaredSymbol(outDesig, ct) is ILocalSymbol outLocal
              && IsUsableType(outLocal.Type, ctx.SemanticModel.Compilation))
          {
            var typeFqn = outLocal.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var origName = outDesig.Identifier.ValueText;
            var pvIdx = patternBindings.Count;
            var preDeclName = $"__pv{pvIdx}_{origName}";
            var tmpName = $"__pvtmp{pvIdx}_{origName}";

            (designationRenames ??= new Dictionary<string, string>())[origName] = tmpName;
            patternBindings[origName] = new LambdaBinding($"(({typeFqn}){preDeclName}!)", preDeclName);
            patternVarDecls.Add((typeFqn, preDeclName, tmpName));
          }
        }
      }

      var condition = compiler.Compile(expression, patternBindings, designationRenames);

      if (condition == null)
      {
        return null;
      }

      var leafNegated = false;
      var leafCore = expression;

      while (leafCore is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
      {
        leafNegated = !leafNegated;
        leafCore = StripParens(negation.Operand);
      }

      // Kinds that don't recompute negation (Contains, Bool, ...) read the pre-set value.
      var subCall = new InterceptedCall { Negated = leafNegated };
      var classified = ClassifyForm(ctx, leafCore, leafNegated, subCall, compiler, ct, bindings: patternBindings, leafContext: true);

      var part = new SplitPart
      {
        LeafSource = expression.ToString(),
        ConditionSource = condition,
        SubCall = classified ? subCall : null,
        StepsSource = new ExceptionStepWalker(ctx.SemanticModel, compiler, expression, ct).CollectSource(expression),
        ProbeSource = BuildCustomProbe(ctx, leafCore, leafNegated, compiler, ct),
      };

      part.Locals.AddRange(call.CapturedLocals.Skip(localsBefore));
      part.PatternVars.AddRange(patternVarDecls);

      return part;
    }

    private static (string Label, string Op) GetRelationalParts(SyntaxKind kind) => kind switch
    {
      SyntaxKind.LessThanToken => ("less than", "<"),
      SyntaxKind.LessThanEqualsToken => ("less than or equal to", "<="),
      SyntaxKind.GreaterThanToken => ("greater than", ">"),
      _ => ("greater than or equal to", ">="),
    };

    /// <summary>
    /// Expands a BinaryPatternSyntax(and) into a left-to-right AndAlso sub-tree.
    /// Each arm is expanded by ExpandAndArm, which recurses for nested and-patterns.
    /// </summary>
    private static SplitPart? BuildAndPatternTree(
      GeneratorSyntaxContext ctx,
      ExpressionSyntax subject,
      BinaryPatternSyntax andPat,
      InterceptedCall call,
      OperandCompiler compiler,
      CancellationToken ct,
      IReadOnlyDictionary<string, LambdaBinding>? bindings)
    {
      var left = ExpandAndArm(ctx, subject, andPat.Left, call, compiler, ct, bindings);
      if (left == null) return null;
      var right = ExpandAndArm(ctx, subject, andPat.Right, call, compiler, ct, bindings);
      if (right == null) return null;
      return new SplitPart { Kind = "AndAlso", Left = left, Right = right };
    }

    private static SplitPart? ExpandAndArm(
      GeneratorSyntaxContext ctx,
      ExpressionSyntax subject,
      PatternSyntax arm,
      InterceptedCall call,
      OperandCompiler compiler,
      CancellationToken ct,
      IReadOnlyDictionary<string, LambdaBinding>? bindings)
    {
      if (arm is BinaryPatternSyntax nested && nested.IsKind(SyntaxKind.AndPattern))
        return BuildAndPatternTree(ctx, subject, nested, call, compiler, ct, bindings);
      return BuildIsPatternArmLeaf(ctx, subject, arm, call, compiler, ct, bindings);
    }

    /// <summary>
    /// Builds a SplitPart leaf from one arm of an and-pattern or a standalone is-sub-pattern.
    /// Handles relational (is >= 0), constant (is 42), and null (is null / is not null) arms.
    /// </summary>
    private static SplitPart? BuildIsPatternArmLeaf(
      GeneratorSyntaxContext ctx,
      ExpressionSyntax subject,
      PatternSyntax pattern,
      InterceptedCall call,
      OperandCompiler compiler,
      CancellationToken ct,
      IReadOnlyDictionary<string, LambdaBinding>? bindings)
    {
      var localsBefore = call.CapturedLocals.Count;

      var negated = false;
      while (pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notPat)
      {
        negated = !negated;
        pattern = notPat.Pattern;
      }

      var compiledSubject = compiler.Compile(subject, bindings);
      if (compiledSubject == null) return null;

      var subjectDisplay = subject.ToString();
      SplitPart? part = null;

      switch (pattern)
      {
        case RelationalPatternSyntax relational when !negated:
        {
          var (compLabel, opStr) = GetRelationalParts(relational.OperatorToken.Kind());
          var compiledRhs = compiler.Compile(relational.Expression, bindings);
          if (compiledRhs == null) return null;
          part = new SplitPart
          {
            LeafSource = $"{subjectDisplay} is {opStr} {relational.Expression}",
            ConditionSource = $"{compiledSubject} {opStr} {compiledRhs}",
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Comparison,
              ComparisonLabel = compLabel,
              LeftSource = compiledSubject,
              LeftDisplay = subjectDisplay,
              RightSource = compiledRhs,
              RightDisplay = relational.Expression.ToString(),
              RightIsConstant = relational.Expression is LiteralExpressionSyntax,
            }
          };
          break;
        }

        case ConstantPatternSyntax nullConst when IsNullOrDefaultLiteral(nullConst.Expression):
        {
          var leftType = ctx.SemanticModel.GetTypeInfo(subject, ct).Type;
          if (leftType is not { IsReferenceType: true } && !IsNullableValueType(leftType)) return null;
          part = new SplitPart
          {
            LeafSource = $"{subjectDisplay} is {(negated ? "not " : "")}null",
            ConditionSource = $"{compiledSubject} {(negated ? "!=" : "==")} null",
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Null,
              Negated = !negated,
              LeftSource = compiledSubject,
              LeftDisplay = subjectDisplay,
            }
          };
          break;
        }

        case ConstantPatternSyntax otherConst when !IsNullOrDefaultLiteral(StripParens(subject)):
        {
          var compiledConst = compiler.Compile(otherConst.Expression, bindings);
          if (compiledConst == null) return null;
          var condExpr = negated
            ? $"!({compiledSubject} == {compiledConst})"
            : $"{compiledSubject} == {compiledConst}";
          part = new SplitPart
          {
            LeafSource = $"{subjectDisplay} is {(negated ? "not " : "")}{otherConst.Expression}",
            ConditionSource = condExpr,
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Equality,
              Negated = negated,
              LeftSource = compiledSubject,
              LeftDisplay = subjectDisplay,
              RightSource = compiledConst,
              RightDisplay = otherConst.Expression.ToString(),
              RightIsConstant = otherConst.Expression is LiteralExpressionSyntax,
            }
          };
          break;
        }
      }

      if (part != null)
        part.Locals.AddRange(call.CapturedLocals.Skip(localsBefore));

      return part;
    }

    /// <summary>
    /// Builds a SplitPart sub-tree for a typed property pattern (obj is User { Name: "Bob" }).
    /// Emits: one type-check leaf (with a pre-declared cast variable) + one leaf per property sub-pattern.
    /// </summary>
    private static SplitPart? BuildPropertyPatternSubTree(
      GeneratorSyntaxContext ctx,
      ExpressionSyntax subject,
      RecursivePatternSyntax recursive,
      InterceptedCall call,
      OperandCompiler compiler,
      CancellationToken ct,
      Dictionary<string, LambdaBinding>? patternBindings)
    {
      var localsBefore = call.CapturedLocals.Count;
      var compiledSubject = compiler.Compile(subject, patternBindings);
      if (compiledSubject == null) return null;

      var subjectLocalsEnd = call.CapturedLocals.Count;
      var subjectDisplay = subject.ToString();
      SplitPart? result = null;
      string receiver;

      if (recursive.Type != null)
      {
        var checkedType = ctx.SemanticModel.GetTypeInfo(recursive.Type, ct).Type;
        if (checkedType == null || !IsUsableType(checkedType, ctx.SemanticModel.Compilation)) return null;

        var typeAccessor = compiler.TypeAccessor(checkedType);
        if (typeAccessor == null) return null;

        var typeFqn = checkedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Pre-declare a cast variable via PatternVars so it lives outside the try block
        // and subsequent property leaves can reference it.
        patternBindings ??= new Dictionary<string, LambdaBinding>();
        var pvIndex = patternBindings.Count;
        var castPreDecl = $"__pv{pvIndex}_cast";
        var castTmp = $"__pvtmp{pvIndex}_cast";
        patternBindings[castPreDecl] = new LambdaBinding($"(({typeFqn}){castPreDecl}!)", castPreDecl);

        var typeCheckLeaf = new SplitPart
        {
          LeafSource = $"{subjectDisplay} is {checkedType.Name}",
          ConditionSource = $"{compiledSubject} is {typeFqn} {castTmp}",
          SubCall = new InterceptedCall
          {
            Kind = InterceptionKind.Is,
            TypeAccessor = typeAccessor,
            LeftSource = compiledSubject,
            LeftDisplay = subjectDisplay,
          }
        };
        typeCheckLeaf.Locals.AddRange(call.CapturedLocals.Skip(localsBefore));
        typeCheckLeaf.PatternVars.Add((typeFqn, castPreDecl, castTmp));

        result = typeCheckLeaf;
        receiver = $"(({typeFqn}){castPreDecl}!)";
      }
      else
      {
        // No type specifier: use the subject directly as the receiver.
        receiver = compiledSubject;
      }

      if (recursive.PropertyPatternClause == null) return result;

      foreach (var subpat in recursive.PropertyPatternClause.Subpatterns)
      {
        if (subpat.NameColon == null) return null; // positional patterns not yet supported

        var propName = subpat.NameColon.Name.Identifier.ValueText;
        var propLocalsBefore = call.CapturedLocals.Count;
        var propLeaf = BuildPropertyLeaf(
          $"{receiver}.{propName}", $"{subjectDisplay}.{propName}",
          subpat.Pattern, compiler, patternBindings);
        if (propLeaf == null) return null;

        // First leaf in the no-type path carries the subject locals (e.g. person: ...).
        if (result == null)
          propLeaf.Locals.AddRange(call.CapturedLocals.GetRange(localsBefore, subjectLocalsEnd - localsBefore));
        propLeaf.Locals.AddRange(call.CapturedLocals.Skip(propLocalsBefore));

        result = result == null
          ? propLeaf
          : new SplitPart { Kind = "AndAlso", Left = result, Right = propLeaf };
      }

      return result;
    }

    /// <summary>
    /// Builds a SplitPart leaf for one property sub-pattern clause.
    /// leftSource is the pre-computed receiver (e.g. "((User)__pv0_cast!).Name");
    /// leftDisplay is the user-facing form (e.g. "obj.Name").
    /// </summary>
    private static SplitPart? BuildPropertyLeaf(
      string leftSource,
      string leftDisplay,
      PatternSyntax pattern,
      OperandCompiler compiler,
      IReadOnlyDictionary<string, LambdaBinding>? bindings)
    {
      var negated = false;
      while (pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notPat)
      {
        negated = !negated;
        pattern = notPat.Pattern;
      }

      switch (pattern)
      {
        case ConstantPatternSyntax nullConst when IsNullOrDefaultLiteral(nullConst.Expression):
          return new SplitPart
          {
            LeafSource = $"{leftDisplay} is {(negated ? "not " : "")}null",
            ConditionSource = $"{leftSource} {(negated ? "!=" : "==")} null",
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Null,
              Negated = !negated,
              LeftSource = leftSource,
              LeftDisplay = leftDisplay,
            }
          };

        case ConstantPatternSyntax otherConst:
        {
          var compiledConst = compiler.Compile(otherConst.Expression, bindings);
          if (compiledConst == null) return null;
          var condExpr = negated ? $"!({leftSource} == {compiledConst})" : $"{leftSource} == {compiledConst}";
          return new SplitPart
          {
            LeafSource = $"{leftDisplay} is {(negated ? "not " : "")}{otherConst.Expression}",
            ConditionSource = condExpr,
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Equality,
              Negated = negated,
              LeftSource = leftSource,
              LeftDisplay = leftDisplay,
              RightSource = compiledConst,
              RightDisplay = otherConst.Expression.ToString(),
              RightIsConstant = otherConst.Expression is LiteralExpressionSyntax,
            }
          };
        }

        case RelationalPatternSyntax relational when !negated:
        {
          var (compLabel, opStr) = GetRelationalParts(relational.OperatorToken.Kind());
          var compiledRhs = compiler.Compile(relational.Expression, bindings);
          if (compiledRhs == null) return null;
          return new SplitPart
          {
            LeafSource = $"{leftDisplay} is {opStr} {relational.Expression}",
            ConditionSource = $"{leftSource} {opStr} {compiledRhs}",
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Comparison,
              ComparisonLabel = compLabel,
              LeftSource = leftSource,
              LeftDisplay = leftDisplay,
              RightSource = compiledRhs,
              RightDisplay = relational.Expression.ToString(),
              RightIsConstant = relational.Expression is LiteralExpressionSyntax,
            }
          };
        }

        // Nested property pattern: { Prop: { SubProp: value } }
        // Emit a non-null guard for the intermediate receiver, then one leaf per sub-property.
        // Recurses so arbitrarily deep nesting works. No type specifier: handled by the
        // type-check leaf in the enclosing BuildPropertyPatternSubTree call.
        case RecursivePatternSyntax { Type: null, PropertyPatternClause: { } nestedClause } when !negated:
        {
          SplitPart chain = new SplitPart
          {
            LeafSource = $"{leftDisplay} is not null",
            ConditionSource = $"{leftSource} != null",
            SubCall = new InterceptedCall
            {
              Kind = InterceptionKind.Null,
              Negated = false, // Negated=false → NullFailure(expectedNull:false) → "expected non-null"
              LeftSource = leftSource,
              LeftDisplay = leftDisplay,
            }
          };

          foreach (var subpat in nestedClause.Subpatterns)
          {
            if (subpat.NameColon == null) return null;
            var propName = subpat.NameColon.Name.Identifier.ValueText;
            var subLeaf = BuildPropertyLeaf(
              $"{leftSource}.{propName}", $"{leftDisplay}.{propName}",
              subpat.Pattern, compiler, bindings);
            if (subLeaf == null) return null;
            chain = new SplitPart { Kind = "AndAlso", Left = chain, Right = subLeaf };
          }

          return chain;
        }

        default:
          return null;
      }
    }

    private static string Quote(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    /// <summary>
    /// Builds a SplitPart sub-tree for a list pattern (arr is [1, _, > 0]).
    /// Emits: one length check leaf, one capture leaf per var/declaration binding (via `is var`
    /// which always matches), and one leaf per non-discard, non-slice, non-var element pattern.
    /// </summary>
    private static SplitPart? BuildListPatternSubTree(
      GeneratorSyntaxContext ctx,
      ExpressionSyntax subject,
      ListPatternSyntax listPat,
      InterceptedCall call,
      OperandCompiler compiler,
      CancellationToken ct,
      Dictionary<string, LambdaBinding>? patternBindings)
    {
      var localsBefore = call.CapturedLocals.Count;

      var compiledSubject = compiler.Compile(subject, patternBindings);
      if (compiledSubject == null) return null;

      var subjectLocalsEnd = call.CapturedLocals.Count;

      var subjectType = ctx.SemanticModel.GetTypeInfo(subject, ct).Type;
      if (subjectType == null) return null;

      var lengthProp = GetCollectionLengthName(subjectType);
      if (lengthProp == null) return null;

      // ListPatternSyntax.Patterns is the direct SeparatedSyntaxList<PatternSyntax> of elements.
      var subpatterns = listPat.Patterns;

      var sliceIndex = -1;
      for (var i = 0; i < subpatterns.Count; i++)
      {
        if (subpatterns[i] is SlicePatternSyntax)
        {
          sliceIndex = i;
          break;
        }
      }

      var hasSlice = sliceIndex >= 0;
      var fixedCount = hasSlice ? subpatterns.Count - 1 : subpatterns.Count;
      var subjectDisplay = subject.ToString();

      SplitPart result = hasSlice
        ? new SplitPart
        {
          LeafSource = $"{subjectDisplay}.{lengthProp} >= {fixedCount}",
          ConditionSource = $"{compiledSubject}.{lengthProp} >= {fixedCount}",
          SubCall = new InterceptedCall
          {
            Kind = InterceptionKind.Comparison,
            ComparisonLabel = "greater than or equal to",
            LeftSource = $"{compiledSubject}.{lengthProp}",
            LeftDisplay = $"{subjectDisplay}.{lengthProp}",
            RightSource = fixedCount.ToString(),
            RightDisplay = fixedCount.ToString(),
            RightIsConstant = true,
          }
        }
        : new SplitPart
        {
          LeafSource = $"{subjectDisplay}.{lengthProp} == {fixedCount}",
          ConditionSource = $"{compiledSubject}.{lengthProp} == {fixedCount}",
          SubCall = new InterceptedCall
          {
            Kind = InterceptionKind.Equality,
            LeftSource = $"{compiledSubject}.{lengthProp}",
            LeftDisplay = $"{subjectDisplay}.{lengthProp}",
            RightSource = fixedCount.ToString(),
            RightDisplay = fixedCount.ToString(),
            RightIsConstant = true,
          }
        };
      result.Locals.AddRange(call.CapturedLocals.Skip(localsBefore));

      // Var/declaration captures: register in patternBindings and emit a capture leaf that
      // uses `arr[i] is var __pvtmpN_name` (always matches) to assign the element to a
      // pre-declared outer variable, making the binding available in subsequent &&-conjuncts.
      if (patternBindings != null)
      {
        for (var i = 0; i < subpatterns.Count; i++)
        {
          var pat = subpatterns[i];
          if (pat is SlicePatternSyntax or DiscardPatternSyntax) continue;

          // Extract the designation (var x or T x). var _ uses DiscardDesignationSyntax, not
          // SingleVariableDesignationSyntax, so the patterns below already exclude it.
          var designation = pat switch
          {
            VarPatternSyntax { Designation: SingleVariableDesignationSyntax vd } => vd,
            DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax dd } => dd,
            _ => null,
          };
          if (designation == null) continue;

          var origName = designation.Identifier.ValueText;
          if (ctx.SemanticModel.GetDeclaredSymbol(designation, ct) is not ILocalSymbol elemLocal) continue;
          if (!IsUsableType(elemLocal.Type, ctx.SemanticModel.Compilation)) continue;

          // Strip outer Nullable<T> so the emitter's appended ? doesn't produce T??.
          var elemType = elemLocal.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableT
            ? nullableT.TypeArguments[0]
            : elemLocal.Type;
          var typeFqn = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

          var pvIndex = patternBindings.Count;
          var preDeclName = $"__pv{pvIndex}_{origName}";
          var tmpName = $"__pvtmp{pvIndex}_{origName}";

          patternBindings[origName] = new LambdaBinding($"(({typeFqn}){preDeclName}!)", preDeclName);

          var indexStr = GetListElementIndexStr(i, sliceIndex, subpatterns.Count);
          var captureLocalsBefore = call.CapturedLocals.Count;
          var captureLeaf = new SplitPart
          {
            LeafSource = $"{subjectDisplay}[{indexStr}] captured as {origName}",
            ConditionSource = $"{compiledSubject}[{indexStr}] is var {tmpName}",
          };
          captureLeaf.PatternVars.Add((typeFqn, preDeclName, tmpName));
          captureLeaf.Locals.AddRange(call.CapturedLocals.GetRange(localsBefore, subjectLocalsEnd - localsBefore));
          captureLeaf.Locals.AddRange(call.CapturedLocals.Skip(captureLocalsBefore));
          result = new SplitPart { Kind = "AndAlso", Left = result, Right = captureLeaf };
        }
      }

      // Element check leaves for non-discard, non-slice, non-var/declaration subpatterns.
      for (var i = 0; i < subpatterns.Count; i++)
      {
        var pat = subpatterns[i];
        if (pat is DiscardPatternSyntax or SlicePatternSyntax or VarPatternSyntax or DeclarationPatternSyntax) continue;

        var indexStr = GetListElementIndexStr(i, sliceIndex, subpatterns.Count);
        var elementSource = $"{compiledSubject}[{indexStr}]";
        var displayIndex = hasSlice && i > sliceIndex ? $"^{subpatterns.Count - i}" : i.ToString();
        var elementDisplay = $"{subjectDisplay}[{displayIndex}]";

        var elemLocalsBefore = call.CapturedLocals.Count;
        var elemLeaf = BuildPropertyLeaf(elementSource, elementDisplay, pat, compiler, patternBindings);
        if (elemLeaf == null) return null;

        elemLeaf.Locals.AddRange(call.CapturedLocals.GetRange(localsBefore, subjectLocalsEnd - localsBefore));
        elemLeaf.Locals.AddRange(call.CapturedLocals.Skip(elemLocalsBefore));
        result = new SplitPart { Kind = "AndAlso", Left = result, Right = elemLeaf };
      }

      return result;
    }

    /// <summary>
    /// Returns the C# index expression for a list element at position i.
    /// Before the slice (or no slice): 0-based index as string. After the slice: "^j" from-end.
    /// </summary>
    private static string GetListElementIndexStr(int i, int sliceIndex, int total)
    {
      if (sliceIndex < 0 || i < sliceIndex) return i.ToString();
      return $"^{total - i}";
    }

    /// <summary>
    /// Finds the property name for the collection's length or count ("Length" or "Count").
    /// Checks the type itself, its base-type chain, then its interfaces.
    /// Returns null if no suitable int-returning property is found.
    /// </summary>
    private static string? GetCollectionLengthName(ITypeSymbol type)
    {
      if (type is IArrayTypeSymbol) return "Length";

      for (var t = type; t != null; t = t.BaseType)
      {
        foreach (var member in t.GetMembers())
        {
          if (member is IPropertySymbol
              {
                Parameters.IsEmpty: true,
                IsStatic: false,
                GetMethod: not null,
                Type.SpecialType: SpecialType.System_Int32
              } prop && prop.Name is "Length" or "Count")
          {
            return prop.Name;
          }
        }
      }

      foreach (var iface in type.AllInterfaces)
      {
        foreach (var member in iface.GetMembers())
        {
          if (member is IPropertySymbol
              {
                Parameters.IsEmpty: true,
                IsStatic: false,
                GetMethod: not null,
                Type.SpecialType: SpecialType.System_Int32
              } prop && prop.Name is "Length" or "Count")
          {
            return prop.Name;
          }
        }
      }

      return null;
    }

    private static bool IsTaskOfBool(ITypeSymbol type)
    {
      return type is INamedTypeSymbol { Name: "Task", Arity: 1 } task
             && task.TypeArguments[0].SpecialType == SpecialType.System_Boolean
             && task.ContainingNamespace is { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };
    }

    /// <summary>
    /// Whether the fragment directly contains an await (one not nested inside a lambda of
    /// its own). Such code can only be pasted into the async render callback of an async
    /// interceptor — not into the synchronous helper lambdas (exception steps,
    /// custom-pattern probes), which skip it and degrade gracefully.
    /// </summary>
    internal static bool ContainsAwait(SyntaxNode node)
    {
      return node.DescendantNodesAndSelf(n => n == node || n is not AnonymousFunctionExpressionSyntax)
        .OfType<AwaitExpressionSyntax>()
        .Any();
    }

    /// <summary>
    /// Escapes a captured name for use as an identifier in generated code. Symbol names of
    /// verbatim identifiers lack the @ (a local `@lock` is named "lock"), so emitting them
    /// bare would put a keyword where an identifier is expected. Display strings and
    /// closure-field lookups keep the raw name.
    /// </summary>
    internal static string Identifier(string name)
      => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    /// <summary>
    /// Determines which pattern the (negation-stripped) assertion body matches, compiles
    /// the operands the pattern needs, and fills the pattern-specific fields of the call.
    /// Returns false when the body is not a whitelisted form or an operand can't be compiled.
    /// When classifying an All() filter body as a per-item sub-assertion, `bindings` maps
    /// the lambda parameter to its bound value and `display` rewrites it to "item" in
    /// displayed sources (AllPattern's NamedConstantExpression equivalent).
    /// </summary>
    private static bool ClassifyForm(GeneratorSyntaxContext ctx, ExpressionSyntax core, bool outerNegated,
      InterceptedCall call, OperandCompiler compiler, CancellationToken ct,
      IReadOnlyDictionary<string, LambdaBinding>? bindings = null,
      System.Func<ExpressionSyntax, string>? display = null,
      bool leafContext = false)
    {
      switch (core)
      {
        case BinaryExpressionSyntax logical when IsSplittableLogical(logical, ctx, ct):
        {
          // `a && b` is the documented way to combine multiple asserts in one statement:
          // each conjunct is split into its own leaf assertion with operator semantics
          // (AssertionTreeProvider/AssertionTreeExecutor parity). Negated composites and
          // nested sub-classifications stay opaque.
          if (outerNegated || bindings != null)
          {
            return false;
          }

          call.Kind = InterceptionKind.Split;

          // For pure &&-only chains, pattern variables declared in one conjunct (obj is T u)
          // can be used in subsequent conjuncts (u.Name == "Bob"). The bindings accumulate
          // left-to-right and are only safe with &&-short-circuit semantics. Mixed chains
          // (||, &, |) don't get this accumulator; cross-conjunct pattern vars there degrade
          // to Opaque (correct — C# scoping is more complex).
          var patternBindings = IsPureAndAlsoSyntax(logical) ? new Dictionary<string, LambdaBinding>() : null;
          call.SplitRoot = BuildSplitPart(ctx, logical, call, compiler, ct, patternBindings);
          return call.SplitRoot != null;
        }

        case BinaryExpressionSyntax binary when binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression:
        {
          var isNotEquals = binary.IsKind(SyntaxKind.NotEqualsExpression);
          var left = StripParens(binary.Left);
          var right = StripParens(binary.Right);

          if (IsNullOrDefaultLiteral(left))
          {
            return false;
          }

          if (IsNullOrDefaultLiteral(right))
          {
            // Null-check routing (NullPattern): `x == null`, `x != default`, ... The old
            // pattern only matched reference types (plus nullables for the null literal);
            // `someStruct == default` stays degraded.
            var leftType = ctx.SemanticModel.GetTypeInfo(left, ct).Type;

            var nullCheckable = right.IsKind(SyntaxKind.NullLiteralExpression)
              ? leftType is { IsReferenceType: true } || IsNullableValueType(leftType)
              : leftType is { IsReferenceType: true };

            if (!nullCheckable)
            {
              return false;
            }

            call.Kind = InterceptionKind.Null;
            call.Negated = !isNotEquals ^ outerNegated; // true = expected null
            return SetLeft(call, compiler, binary.Left, bindings, display);
          }

          if (TryGetLengthAccess(left, out var countLabel, out var operand, out var filterSource))
          {
            // The old LengthPattern did not match negated forms.
            if (outerNegated)
            {
              return false;
            }

            call.Kind = InterceptionKind.Length;
            call.CountLabel = countLabel;
            call.OperandDisplay = display?.Invoke(operand) ?? operand.ToString();
            call.FilterSource = filterSource;
            call.ComparisonLabel = isNotEquals ? "not equal to" : "equal to";
            return SetLeft(call, compiler, binary.Left, bindings, display) && SetRight(call, compiler, binary.Right, right, bindings, display);
          }

          if (IsLengthOrCountAccess(left) || IsLengthOrCountAccess(right))
          {
            // LongCount and friends: routed to other patterns historically; stay degraded.
            return false;
          }

          call.Kind = InterceptionKind.Equality;
          call.Negated = isNotEquals ^ outerNegated;
          return SetLeft(call, compiler, binary.Left, bindings, display) && SetRight(call, compiler, binary.Right, right, bindings, display);
        }

        case BinaryExpressionSyntax binary when binary.Kind() is SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
          or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression:
        {
          // The old comparison/length patterns did not match negated forms.
          if (outerNegated)
          {
            return false;
          }

          var comparisonLabel = binary.Kind() switch
          {
            SyntaxKind.LessThanExpression => "less than",
            SyntaxKind.LessThanOrEqualExpression => "less than or equal to",
            SyntaxKind.GreaterThanExpression => "greater than",
            _ => "greater than or equal to",
          };

          var left = StripParens(binary.Left);
          var right = StripParens(binary.Right);

          if (IsNullOrDefaultLiteral(left) || IsNullOrDefaultLiteral(right))
          {
            return false;
          }

          call.ComparisonLabel = comparisonLabel;

          if (TryGetLengthAccess(left, out var countLabel, out var operand, out var filterSource))
          {
            call.Kind = InterceptionKind.Length;
            call.CountLabel = countLabel;
            call.OperandDisplay = display?.Invoke(operand) ?? operand.ToString();
            call.FilterSource = filterSource;
          }
          else
          {
            call.Kind = InterceptionKind.Comparison;
          }

          return SetLeft(call, compiler, binary.Left, bindings, display) && SetRight(call, compiler, binary.Right, right, bindings, display);
        }

        case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression):
        {
          if (ctx.SemanticModel.GetSymbolInfo(binary.Right, ct).Symbol is not ITypeSymbol checkedType)
          {
            return false;
          }

          if (checkedType.SpecialType == SpecialType.System_Object)
          {
            // `x is object` is a null check (NullPattern); negated means "expected null".
            call.Kind = InterceptionKind.Null;
            call.Negated = outerNegated;
            return SetLeft(call, compiler, binary.Left, bindings, display);
          }

          call.Kind = InterceptionKind.Is;
          call.TypeAccessor = compiler.TypeAccessor(checkedType);
          return call.TypeAccessor != null && SetLeft(call, compiler, binary.Left, bindings, display);
        }

        // C# 7+ pattern matching: `obj is Type`, `obj is Type u`, `obj is null`, `n is > 18`,
        // `obj is User { Name: "Bob" }`, `n is >= 0 and <= 100`, `obj is not null`, etc.
        case IsPatternExpressionSyntax isPattern:
        {
          // Peel outer not-patterns to normalize; patternNegated tracks the parity.
          var patternNegated = false;
          PatternSyntax innerPat = isPattern.Pattern;
          while (innerPat is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notWrapper)
          {
            patternNegated = !patternNegated;
            innerPat = notWrapper.Pattern;
          }

          // and-pattern (n is >= 0 and <= 100): expand into a conjunction of leaves.
          // Only at the top level; negated, leaf-context, and nested-bindings all block it.
          if (!patternNegated && !leafContext && !outerNegated && bindings == null
              && innerPat is BinaryPatternSyntax andBin && andBin.IsKind(SyntaxKind.AndPattern))
          {
            call.Kind = InterceptionKind.Split;
            var innerBindings = new Dictionary<string, LambdaBinding>();
            call.SplitRoot = BuildSplitPart(ctx, isPattern, call, compiler, ct, innerBindings);
            return call.SplitRoot != null;
          }

          // Property pattern (obj is User { Name: "Bob" } or just { Name: "Bob" }):
          // expand into optional type-check + sub-leaves. Type specifier is not required.
          if (!patternNegated && !leafContext && !outerNegated && bindings == null
              && innerPat is RecursivePatternSyntax { PropertyPatternClause: not null })
          {
            call.Kind = InterceptionKind.Split;
            var innerBindings = new Dictionary<string, LambdaBinding>();
            call.SplitRoot = BuildSplitPart(ctx, isPattern, call, compiler, ct, innerBindings);
            return call.SplitRoot != null;
          }

          // List pattern (arr is [1, _, > 0]): expand into length check + element sub-leaves.
          if (!patternNegated && !leafContext && !outerNegated && bindings == null
              && innerPat is ListPatternSyntax)
          {
            call.Kind = InterceptionKind.Split;
            var innerBindings = new Dictionary<string, LambdaBinding>();
            call.SplitRoot = BuildSplitPart(ctx, isPattern, call, compiler, ct, innerBindings);
            return call.SplitRoot != null;
          }

          // Simple sub-patterns: relational, constant (including null), and their not-forms.
          switch (innerPat)
          {
            case RelationalPatternSyntax relational when !patternNegated && !outerNegated:
            {
              var (compLabel, opStr) = GetRelationalParts(relational.OperatorToken.Kind());
              call.Kind = InterceptionKind.Comparison;
              call.ComparisonLabel = compLabel;
              return SetLeft(call, compiler, isPattern.Expression, bindings, display)
                     && SetRight(call, compiler, relational.Expression, StripParens(relational.Expression), bindings, display);
            }

            case ConstantPatternSyntax nullConst when IsNullOrDefaultLiteral(nullConst.Expression):
            {
              var leftType = ctx.SemanticModel.GetTypeInfo(isPattern.Expression, ct).Type;
              if (leftType is not { IsReferenceType: true } && !IsNullableValueType(leftType)) return false;
              // is null  → Negated=true (expected null);
              // is not null → Negated=false (expected non-null).
              call.Kind = InterceptionKind.Null;
              call.Negated = !patternNegated ^ outerNegated;
              return SetLeft(call, compiler, isPattern.Expression, bindings, display);
            }

            case ConstantPatternSyntax otherConst when !IsNullOrDefaultLiteral(StripParens(isPattern.Expression)):
            {
              call.Kind = InterceptionKind.Equality;
              call.Negated = patternNegated ^ outerNegated;
              return SetLeft(call, compiler, isPattern.Expression, bindings, display)
                     && SetRight(call, compiler, otherConst.Expression, StripParens(otherConst.Expression), bindings, display);
            }
          }

          // Type-check patterns: obj is Type (Is kind) or obj is object / is not object (Null kind).
          ITypeSymbol? checkedType = innerPat switch
          {
            TypePatternSyntax typePat => ctx.SemanticModel.GetTypeInfo(typePat.Type, ct).Type,
            DeclarationPatternSyntax declPat when !patternNegated => ctx.SemanticModel.GetTypeInfo(declPat.Type, ct).Type,
            _ => null,
          };

          if (checkedType == null) return false;

          if (checkedType.SpecialType == SpecialType.System_Object)
          {
            // `x is object` = non-null check (Negated=false);
            // `x is not object` = null check (Negated=true).
            call.Kind = InterceptionKind.Null;
            call.Negated = patternNegated ^ outerNegated;
            return SetLeft(call, compiler, isPattern.Expression, bindings, display);
          }

          // `obj is not Type` / `obj is not Type u` degrade — Is kind has no Negated flag.
          if (patternNegated) return false;

          call.Kind = InterceptionKind.Is;
          call.TypeAccessor = compiler.TypeAccessor(checkedType);
          return call.TypeAccessor != null && SetLeft(call, compiler, isPattern.Expression, bindings, display);
        }

        case InvocationExpressionSyntax
        {
          Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" } equalsAccess,
          ArgumentList.Arguments.Count: 1
        } equalsCall when ctx.SemanticModel.GetSymbolInfo(equalsCall, ct).Symbol is IMethodSymbol
        {
          Name: "Equals", IsStatic: false, ReturnType.SpecialType: SpecialType.System_Boolean, Parameters.Length: 1
        }:
        {
          var right = StripParens(equalsCall.ArgumentList.Arguments[0].Expression);

          if (IsNullOrDefaultLiteral(right) || IsNullOrDefaultLiteral(StripParens(equalsAccess.Expression)))
          {
            return false;
          }

          call.Kind = InterceptionKind.Equality;
          return SetLeft(call, compiler, equalsAccess.Expression, bindings, display)
                 && SetRight(call, compiler, equalsCall.ArgumentList.Arguments[0].Expression, right, bindings, display);
        }

        case InvocationExpressionSyntax { ArgumentList.Arguments.Count: 2 } referenceEqualsCall
          when ctx.SemanticModel.GetSymbolInfo(referenceEqualsCall, ct).Symbol is IMethodSymbol
          {
            Name: "ReferenceEquals", IsStatic: true, Parameters.Length: 2, ContainingType.SpecialType: SpecialType.System_Object
          } && referenceEqualsCall.ArgumentList.Arguments.All(a => a.NameColon == null):
        {
          call.Kind = InterceptionKind.ReferenceEquals;
          return SetLeft(call, compiler, referenceEqualsCall.ArgumentList.Arguments[0].Expression, bindings, display)
                 && SetRight(call, compiler, referenceEqualsCall.ArgumentList.Arguments[1].Expression,
                   StripParens(referenceEqualsCall.ArgumentList.Arguments[1].Expression), bindings, display);
        }

        case InvocationExpressionSyntax methodCall
          when methodCall.Expression is MemberAccessExpressionSyntax methodAccess
               && methodAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
               && methodCall.ArgumentList.Arguments.All(a => a.NameColon == null)
               && ctx.SemanticModel.GetSymbolInfo(methodCall, ct).Symbol is IMethodSymbol calledMethod:
        {
          switch (calledMethod.Name)
          {
            case "Contains" when calledMethod.ReturnType.SpecialType == SpecialType.System_Boolean
                                 && methodCall.ArgumentList.Arguments.Count >= 1
                                 && (!calledMethod.IsStatic || calledMethod.ReducedFrom != null):
            {
              var arg = methodCall.ArgumentList.Arguments[0].Expression;

              call.Kind = InterceptionKind.Contains;
              call.StringInstance = ctx.SemanticModel.GetTypeInfo(methodAccess.Expression, ct).Type?.SpecialType == SpecialType.System_String;
              return SetLeft(call, compiler, methodAccess.Expression, bindings, display) && SetRight(call, compiler, arg, StripParens(arg), bindings, display);
            }

            case "StartsWith" or "EndsWith" when !calledMethod.IsStatic
                                                 && calledMethod.Parameters.Length >= 1
                                                 && calledMethod.Parameters[0].Type.SpecialType == SpecialType.System_String
                                                 && methodCall.ArgumentList.Arguments.Count >= 1:
            {
              var arg = methodCall.ArgumentList.Arguments[0].Expression;

              call.Kind = InterceptionKind.StartsEndsWith;
              call.ComparisonLabel = calledMethod.Name == "StartsWith" ? "start with" : "end with";
              return SetLeft(call, compiler, methodAccess.Expression, bindings, display) && SetRight(call, compiler, arg, StripParens(arg), bindings, display);
            }

            case "Any" when IsLinqEnumerableMethod(calledMethod):
            {
              var arguments = methodCall.ArgumentList.Arguments;

              call.Kind = InterceptionKind.Any;
              call.OperandDisplay = display?.Invoke(methodAccess.Expression) ?? methodAccess.Expression.ToString();
              // The whole call as display: collection locals stay in the LOCALS section
              // (the old LocalsProvider showed them for Any).
              call.LeftDisplay = display?.Invoke(methodCall) ?? methodCall.ToString();

              if (arguments.Count != 0
                  && !(arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax }))
              {
                return false;
              }

              // Collection first so LOCALS lists captures in order of appearance.
              var collection = compiler.Compile(methodAccess.Expression, bindings);

              if (collection == null)
              {
                return false;
              }

              if (arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax filterBody } filterLambda)
              {
                call.FilterSource = filterBody.ToString();

                // Compiling the filter registers its captured locals for the LOCALS
                // section; only the negated form actually evaluates it (for the count).
                var typedFilter = compiler.CompileTypedOnly(filterLambda);

                if (outerNegated)
                {
                  // Needs the count of filter-matching items: typed collection + filter.
                  var typedCollection = compiler.CompileTypedOnly(methodAccess.Expression);

                  if (typedCollection == null || typedFilter == null)
                  {
                    return false;
                  }

                  call.LeftSource = $"global::System.Linq.Enumerable.Count({typedCollection}, {typedFilter})";
                  return true;
                }
              }

              call.LeftSource = $"__A.EnumerableCount({collection})";
              return true;
            }

            case "All" when IsLinqEnumerableMethod(calledMethod)
                            && bindings == null
                            && methodCall.ArgumentList.Arguments.Count == 1
                            && methodCall.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax allFilterBody } allFilterLambda:
            {
              var filterParameter = allFilterLambda switch
              {
                SimpleLambdaExpressionSyntax simple => simple.Parameter,
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1 } singleParameter } => singleParameter[0],
                _ => null,
              };

              if (filterParameter == null
                  || ctx.SemanticModel.GetDeclaredSymbol(filterParameter, ct) is not { } parameterSymbol)
              {
                return false;
              }

              call.Kind = InterceptionKind.All;
              call.Negated = outerNegated;
              call.OperandDisplay = new AnonymousMemberExpander().Visit(methodAccess.Expression)?.ToString()
                                    ?? methodAccess.Expression.ToString();
              // The whole call as display: collection locals stay in the LOCALS section.
              call.LeftDisplay = methodCall.ToString();
              call.FilterSource = allFilterBody.ToString();
              call.CollectionIsMethodCall = StripParens(methodAccess.Expression) is InvocationExpressionSyntax;
              call.LeftSource = compiler.Compile(methodAccess.Expression)!;

              if (call.LeftSource == null)
              {
                return false;
              }

              if (outerNegated)
              {
                // NotAllPattern: expected-only message, no per-item analysis.
                return true;
              }

              var itemBindings = new Dictionary<string, LambdaBinding>
              {
                [parameterSymbol.Name] = new LambdaBinding(
                  IsUsableType(parameterSymbol.Type, ctx.SemanticModel.Compilation)
                    ? $"(({parameterSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})__i)"
                    : null,
                  "__i"),
              };

              // Finding the failing items requires evaluating the filter per item: typed
              // when the element type is nameable, otherwise an Equals-based fallback for
              // (in)equality bodies (anonymous element types).
              if (compiler.CompileTypedOnly(allFilterBody, itemBindings) is { } typedFilter)
              {
                call.AllFilterFunc = $"(__i, __j) => (bool)({typedFilter})";
              }

              // Classify the filter body as a per-item sub-assertion, displayed with the
              // parameter renamed to "item" (the old AllPattern bound the item as a
              // NamedConstantExpression named "item" and re-ran the full analyzer).
              var renamer = new ParamRenameRewriter(ctx.SemanticModel, parameterSymbol, ct);
              System.Func<ExpressionSyntax, string> itemDisplay = e => renamer.Visit(e)?.ToString() ?? e.ToString();

              var subNegated = false;
              var subCore = StripParens(allFilterBody);

              while (subCore is PrefixUnaryExpressionSyntax subNegation && subNegation.IsKind(SyntaxKind.LogicalNotExpression))
              {
                subNegated = !subNegated;
                subCore = StripParens(subNegation.Operand);
              }

              var subCall = new InterceptedCall { Negated = subNegated };

              if (ClassifyForm(ctx, subCore, subNegated, subCall, compiler, ct, itemBindings, itemDisplay))
              {
                call.AllSubCall = subCall;

                if (call.AllFilterFunc == null && subCall.Kind == InterceptionKind.Equality)
                {
                  var equalsCheck = $"__A.ObjectEquals({subCall.LeftSource}, {subCall.RightSource})";
                  call.AllFilterFunc = $"(__i, __j) => {(subCall.Negated ? "!" : "")}{equalsCheck}";
                }
              }

              return call.AllFilterFunc != null;
            }

            // On newer TFMs (first-class spans + OverloadResolutionPriority), array
            // receivers bind to MemoryExtensions.SequenceEqual instead of
            // Enumerable.SequenceEqual; the semantics (element-wise equality, optional
            // comparer) are identical and the runtime diff enumerates the captured
            // values, so both bindings classify the same.
            case "SequenceEqual" when (IsLinqEnumerableMethod(calledMethod) || IsMemoryExtensionsMethod(calledMethod))
                                      && !outerNegated
                                      && methodCall.ArgumentList.Arguments.Count is 1 or 2:
            {
              var arg = methodCall.ArgumentList.Arguments[0].Expression;

              // A genuinely span-typed receiver/argument (not an array converted at the
              // call) is a ref struct: it cannot be captured or enumerated as a value.
              if (ctx.SemanticModel.GetTypeInfo(methodAccess.Expression, ct).Type is { IsRefLikeType: true }
                  || ctx.SemanticModel.GetTypeInfo(arg, ct).Type is { IsRefLikeType: true })
              {
                return false;
              }

              call.Kind = InterceptionKind.SequenceEqual;

              if (!SetLeft(call, compiler, methodAccess.Expression, bindings, display) || !SetRight(call, compiler, arg, StripParens(arg), bindings, display))
              {
                return false;
              }

              if (methodCall.ArgumentList.Arguments.Count == 2)
              {
                call.ComparerSource = compiler.Compile(methodCall.ArgumentList.Arguments[1].Expression);

                if (call.ComparerSource == null)
                {
                  return false;
                }
              }

              call.TypeAccessor = calledMethod.TypeArguments.Length == 1
                ? compiler.TypeAccessor(calledMethod.TypeArguments[0])
                : null;

              return true;
            }

            default:
              return false;
          }
        }

        case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "HasValue" } hasValueAccess
          when hasValueAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
               && IsNullableValueType(ctx.SemanticModel.GetTypeInfo(hasValueAccess.Expression, ct).Type):
        {
          call.Kind = InterceptionKind.HasValue;
          return SetLeft(call, compiler, hasValueAccess.Expression, bindings, display);
        }

        case IdentifierNameSyntax:
        case MemberAccessExpressionSyntax when core.IsKind(SyntaxKind.SimpleMemberAccessExpression):
        {
          // A bare bool member/local (BoolPattern). The operand is compiled only so its
          // captured locals are available for the LOCALS section; the value is not needed
          // (the delegate already evaluated it).
          call.Kind = InterceptionKind.Bool;
          return SetLeft(call, compiler, core, bindings, display);
        }

        default:
          return false;
      }
    }

    private static bool SetLeft(InterceptedCall call, OperandCompiler compiler, ExpressionSyntax operand,
      IReadOnlyDictionary<string, LambdaBinding>? bindings = null, System.Func<ExpressionSyntax, string>? display = null)
    {
      call.LeftSource = compiler.Compile(operand, bindings)!;
      call.LeftDisplay = display?.Invoke(operand) ?? operand.ToString();
      return call.LeftSource != null;
    }

    private static bool SetRight(InterceptedCall call, OperandCompiler compiler, ExpressionSyntax operand, ExpressionSyntax stripped,
      IReadOnlyDictionary<string, LambdaBinding>? bindings = null, System.Func<ExpressionSyntax, string>? display = null)
    {
      call.RightSource = compiler.Compile(operand, bindings)!;
      call.RightDisplay = display?.Invoke(operand) ?? operand.ToString();
      call.RightIsConstant = stripped is LiteralExpressionSyntax;
      return call.RightSource != null;
    }

    /// <summary>
    /// Matches the shapes the old LengthPattern recognized on the left side of a
    /// comparison: array/string .Length, collection .Count, and LINQ .Count() with an
    /// optional expression-bodied lambda filter.
    /// </summary>
    private static bool TryGetLengthAccess(ExpressionSyntax left, out string countLabel, out ExpressionSyntax operand, out string? filterSource)
    {
      countLabel = "";
      operand = left;
      filterSource = null;

      switch (left)
      {
        case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" } lengthAccess
          when lengthAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
          countLabel = "Length";
          operand = lengthAccess.Expression;
          return true;

        case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" } countAccess
          when countAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
          countLabel = "Count";
          operand = countAccess.Expression;
          return true;

        case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" } countCall } countInvocation
          when countCall.IsKind(SyntaxKind.SimpleMemberAccessExpression):
        {
          countLabel = "Count";
          operand = countCall.Expression;

          switch (countInvocation.ArgumentList.Arguments.Count)
          {
            case 0:
              return true;
            case 1 when countInvocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax filterBody }:
              filterSource = filterBody.ToString();
              return true;
            default:
              return false;
          }
        }

        default:
          return false;
      }
    }

    /// <summary>
    /// Compiles a single operand expression into C# for the generated failure path.
    /// First tries the typed strategy (paste the source, fully qualify type references and
    /// reduced extension calls, read captured locals from the closure with a typed cast);
    /// when any name involved is not nameable or a member is not accessible from the
    /// generated file, falls back to the reflective strategy (object-typed values, members
    /// resolved at runtime by name). Captured locals are registered on the call as a side
    /// effect of successful compilation.
    /// </summary>
    private sealed class OperandCompiler
    {
      private readonly SemanticModel _model;
      private readonly Compilation _compilation;
      private readonly SyntaxNode _body;
      private readonly InterceptedCall _call;
      private readonly CancellationToken _ct;

      private const string Runtime = "__A";
      private const string CapturedThis = Runtime + ".GetCapturedThis(__f)";

      public OperandCompiler(SemanticModel model, SyntaxNode body, InterceptedCall call, CancellationToken ct)
      {
        _model = model;
        _compilation = model.Compilation;
        _body = body;
        _call = call;
        _ct = ct;
      }

      /// <summary>Typed strategy only — for code that must keep its static type (LINQ Count with filter).</summary>
      public string? CompileTypedOnly(ExpressionSyntax operand, IReadOnlyDictionary<string, LambdaBinding>? bindings = null)
        => CompileTyped(operand, bindings);

      public string? Compile(ExpressionSyntax operand, IReadOnlyDictionary<string, LambdaBinding>? bindings = null,
        IReadOnlyDictionary<string, string>? designationRenames = null)
      {
        var typed = CompileTyped(operand, bindings, designationRenames);

        if (typed != null)
        {
          return typed;
        }

        var captures = new List<(string Name, string? Type)>();
        var reflective = CompileReflective(operand, captures, bindings);

        if (reflective != null)
        {
          Commit(captures);
        }

        return reflective;
      }

      private string? CompileTyped(ExpressionSyntax operand, IReadOnlyDictionary<string, LambdaBinding>? bindings = null,
        IReadOnlyDictionary<string, string>? designationRenames = null)
      {
        var captures = new List<(string Name, string? Type)>();

        if (!ValidateTypedFragment(operand, captures, bindings))
        {
          return null;
        }

        var rewriter = new TypedRenderRewriter(_model, operand, bindings, _ct, designationRenames);
        var rendered = rewriter.Visit(operand);

        if (rewriter.Failed || rendered == null)
        {
          return null;
        }

        Commit(captures);

        return rendered.ToString();
      }

      /// <summary>
      /// Whether the fragment can be pasted into generated code as-is (modulo the
      /// TypedRenderRewriter's qualification): every node bindable from outside the
      /// declaring type, every type nameable, captured locals/parameters registered.
      /// Shared by operand compilation (expressions) and local-function lifting (whole
      /// declarations).
      /// </summary>
      private bool ValidateTypedFragment(SyntaxNode fragment, List<(string Name, string? Type)> captures,
        IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        // DeclarationExpressions (out var x) are only valid inside an enclosing expression;
        // as a standalone compiled operand they produce non-evaluable syntax like "(object)(T x)".
        if (fragment is DeclarationExpressionSyntax)
        {
          return false;
        }

        foreach (var node in fragment.DescendantNodesAndSelf())
        {
          switch (node)
          {
            case ThisExpressionSyntax:
            case BaseExpressionSyntax:
            case QueryExpressionSyntax:
              return false;

            case AwaitExpressionSyntax awaitExpression:
            {
              // Awaits paste fine into the async render callback, but the generated file
              // has no using directives: awaitables whose GetAwaiter is an extension
              // method would not bind there. Task and friends use an instance GetAwaiter.
              if (_model.GetAwaitExpressionInfo(awaitExpression).GetAwaiterMethod is not { IsStatic: false, ReducedFrom: null })
              {
                return false;
              }

              continue;
            }

            case InvocationExpressionSyntax innerCall:
            {
              if (_model.GetSymbolInfo(innerCall, _ct).Symbol is not IMethodSymbol innerMethod)
              {
                continue; // nameof and friends
              }

              // Calls to local functions bind to the lifted declaration; the name walk
              // below performs the lift.
              if (innerMethod.MethodKind == MethodKind.LocalFunction)
              {
                continue;
              }

              if (!IsAccessibleMember(innerMethod))
              {
                return false;
              }

              // Reduced extension calls are rewritten to fully-qualified static calls,
              // which requires a plain member-access receiver and a nameable static class.
              if (innerMethod.ReducedFrom != null
                  && (innerCall.Expression is not MemberAccessExpressionSyntax extensionAccess
                      || !extensionAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                      || !IsUsableType(innerMethod.ContainingType, _compilation)))
              {
                return false;
              }

              continue;
            }

            // Pasted member accesses bind outside the declaring type, so the member itself
            // must be accessible from generated code (private/protected members are not).
            case MemberAccessExpressionSyntax memberAccess:
              if (_model.GetSymbolInfo(memberAccess, _ct).Symbol is { } memberSymbol && !IsAccessibleMember(memberSymbol))
              {
                return false;
              }

              continue;

            case ElementAccessExpressionSyntax elementAccess:
              if (_model.GetSymbolInfo(elementAccess, _ct).Symbol is { } indexerSymbol && !IsAccessibleMember(indexerSymbol))
              {
                return false;
              }

              continue;

            case ObjectCreationExpressionSyntax creation:
              if (_model.GetSymbolInfo(creation, _ct).Symbol is { } constructorSymbol && !IsAccessibleMember(constructorSymbol))
              {
                return false;
              }

              continue;
          }
        }

        foreach (var name in fragment.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
          // Member names (`Length` in `x.Length`, `Ordinal` in `StringComparison.Ordinal`):
          // the receiver/qualifier determines them.
          if (IsMemberNamePosition(name))
          {
            continue;
          }

          var symbol = _model.GetSymbolInfo(name, _ct).Symbol;

          switch (symbol)
          {
            case INamespaceSymbol:
              continue;

            case ITypeSymbol type:
              // A type that can't be named from the generated file (private nested, say)
              // fails the typed strategy; the reflective one may still handle it.
              if (!IsUsableType(type, _compilation))
              {
                return false;
              }

              continue;

            case IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction:
              // Local functions are not members of anything callable from generated code,
              // but their declaration is right here in the syntax tree: lift it into the
              // generated scope, where this name then binds to the pasted copy.
              if (!TryLiftLocalFunction(localFunction))
              {
                return false;
              }

              continue;

            case ILocalSymbol local when name is IdentifierNameSyntax:
              // Locals declared inside the fragment (pattern variables, lifted local
              // function bodies) are bound by the pasted code itself.
              if (IsDeclaredWithin(local, fragment))
              {
                continue;
              }

              // Pattern-variable binding from a prior &&-chain conjunct: the TypedRenderRewriter
              // replaces this name with the pre-declared outer variable.
              if (bindings != null && bindings.TryGetValue(local.Name, out var pvBinding)
                  && pvBinding.TypedReplacement != null)
              {
                continue;
              }

              // A local declared inside the assertion body but not within this fragment is
              // a pattern variable (or out-var) from another conjunct — it is not a closure
              // field and cannot be read via GetCapturedValue.
              if (IsDeclaredWithin(local, _body))
              {
                return false;
              }

              // Const locals have no closure field (they're baked in as constants).
              if (local.IsConst || !IsUsableType(local.Type, _compilation))
              {
                return false;
              }

              captures.Add((local.Name, local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
              continue;

            case IParameterSymbol param when name is IdentifierNameSyntax:
              // Parameters declared inside the compiled fragment (nested lambdas, lifted
              // local functions) are bound by the pasted code itself.
              if (IsDeclaredWithin(param, fragment))
              {
                continue;
              }

              // Lambda parameters of the assertion body that the caller bound to a value
              // (exception-step item/index/candidate) are rewritten to their replacement.
              if (IsDeclaredWithin(param, _body))
              {
                if (bindings != null && bindings.TryGetValue(param.Name, out var binding)
                    && binding.TypedReplacement != null)
                {
                  continue;
                }

                return false;
              }

              // Parameters of enclosing methods/lambdas are captured like locals.
              if (!IsUsableType(param.Type, _compilation))
              {
                return false;
              }

              captures.Add((param.Name, param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
              continue;

            default:
              // Fields/properties on `this`, statics via `using static`, method groups,
              // range variables, anything unresolved: not typed-compilable; the
              // reflective strategy may still apply.
              return false;
          }
        }

        return true;
      }

      /// <summary>Lift results per local function, memoized by symbol (also breaks recursion cycles).</summary>
      private readonly Dictionary<IMethodSymbol, bool> _liftedLocalFunctions = new(SymbolEqualityComparer.Default);

      /// <summary>
      /// Lifts a local function by pasting its (qualification-rewritten) declaration into
      /// the generated reporting scope. Captured locals bind to the capture declarations
      /// the render scope already makes from the closure; calls to other local functions
      /// lift transitively. Validation is the same whitelist as typed operand pasting, so
      /// bodies touching `this` or inaccessible members reject the lift (degradation).
      /// </summary>
      private bool TryLiftLocalFunction(IMethodSymbol symbol)
      {
        if (_liftedLocalFunctions.TryGetValue(symbol, out var lifted))
        {
          return lifted;
        }

        // Optimistic marker: a self-recursive body finding itself mid-lift is fine —
        // the name will bind to the pasted declaration.
        _liftedLocalFunctions[symbol] = true;

        if (symbol.DeclaringSyntaxReferences.Length != 1
            || symbol.DeclaringSyntaxReferences[0].GetSyntax(_ct) is not LocalFunctionStatementSyntax declaration
            || declaration.SyntaxTree != _body.SyntaxTree)
        {
          return _liftedLocalFunctions[symbol] = false;
        }

        var captures = new List<(string Name, string? Type)>();

        if (!ValidateTypedFragment(declaration, captures, bindings: null))
        {
          return _liftedLocalFunctions[symbol] = false;
        }

        var rewriter = new TypedRenderRewriter(_model, declaration, null, _ct);
        var rendered = rewriter.Visit(declaration);

        if (rewriter.Failed || rendered == null)
        {
          return _liftedLocalFunctions[symbol] = false;
        }

        Commit(captures);
        _call.LiftedLocalFunctions.Add(rendered.ToString());

        return true;
      }

      private bool IsAccessibleMember(ISymbol symbol)
      {
        return symbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol)
               || _compilation.IsSymbolAccessibleWithin(symbol, _compilation.Assembly);
      }

      private static bool IsMemberNamePosition(SimpleNameSyntax name)
      {
        return (name.Parent is MemberAccessExpressionSyntax ma && ma.Name == name)
               || (name.Parent is QualifiedNameSyntax qn && qn.Right == name)
               || name.Parent is MemberBindingExpressionSyntax
               // Named tuple elements ((x: 1, y: 2)) and named arguments: the name is
               // not a free-standing reference, it pastes as written.
               || name.Parent is NameColonSyntax;
      }

      /// <summary>
      /// Renders typed operand code: type/namespace qualifiers become fully-qualified names
      /// and reduced extension calls become static calls (`xs.Count()` to
      /// `global::System.Linq.Enumerable.Count(xs)`), since the generated file has no
      /// using directives.
      /// </summary>
      private sealed class TypedRenderRewriter : CSharpSyntaxRewriter
      {
        private readonly SemanticModel _model;
        private readonly SyntaxNode _fragment;
        private readonly IReadOnlyDictionary<string, LambdaBinding>? _bindings;
        private readonly CancellationToken _ct;
        private readonly IReadOnlyDictionary<string, string>? _designationRenames;

        public bool Failed;

        public TypedRenderRewriter(SemanticModel model, SyntaxNode fragment, IReadOnlyDictionary<string, LambdaBinding>? bindings,
          CancellationToken ct, IReadOnlyDictionary<string, string>? designationRenames = null)
        {
          _model = model;
          _fragment = fragment;
          _bindings = bindings;
          _ct = ct;
          _designationRenames = designationRenames;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
          => RewriteName(node, base.VisitIdentifierName(node));

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
          => RewriteName(node, base.VisitGenericName(node));

        // Rename the declaration variable in is-pattern expressions when the analyzer has
        // assigned a temp name (e.g. `obj is User u` → `obj is User __pvtmp0_u`) to avoid
        // CS0136 with the pre-declared outer pattern variable.
        public override SyntaxNode? VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        {
          if (_designationRenames != null
              && _designationRenames.TryGetValue(node.Identifier.ValueText, out var renamed))
          {
            return node.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(node.Identifier));
          }

          return base.VisitSingleVariableDesignation(node);
        }

        // Out-var declarations (out var x, out T x): GetSymbolInfo on the 'var' keyword
        // returns an inconsistent symbol that can cause the type to render as empty. Use
        // GetTypeInfo on the whole DeclarationExpression, which is always reliable.
        // Applies the designation rename when _designationRenames is set (condition source)
        // and keeps the original name otherwise (ExceptionStep Node lambdas).
        public override SyntaxNode? VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
          if (node.Designation is not SingleVariableDesignationSyntax desig)
          {
            return base.VisitDeclarationExpression(node);
          }

          var inferredType = _model.GetTypeInfo(node, _ct).Type;

          if (inferredType == null || !IsUsableType(inferredType, _model.Compilation))
          {
            return base.VisitDeclarationExpression(node);
          }

          var typeFqn = inferredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          var outName = _designationRenames != null && _designationRenames.TryGetValue(desig.Identifier.ValueText, out var tmpName)
            ? tmpName
            : desig.Identifier.ValueText;

          return SyntaxFactory.DeclarationExpression(
            SyntaxFactory.ParseTypeName(typeFqn).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(outName)));
        }

        private SyntaxNode? RewriteName(SimpleNameSyntax original, SyntaxNode? visited)
        {
          if (IsMemberNamePosition(original))
          {
            return visited;
          }

          var symbol = _model.GetSymbolInfo(original, _ct).Symbol;

          if (symbol is INamespaceOrTypeSymbol namespaceOrType)
          {
            return SyntaxFactory.ParseName(namespaceOrType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
              .WithTriviaFrom(original);
          }

          // Bound lambda parameters (exception-step item/index/candidate) are replaced by
          // their typed replacement expression.
          if (_bindings != null
              && symbol is IParameterSymbol parameter
              && !IsDeclaredWithin(parameter, _fragment)
              && _bindings.TryGetValue(parameter.Name, out var binding)
              && binding.TypedReplacement != null)
          {
            return SyntaxFactory.ParseExpression(binding.TypedReplacement).WithTriviaFrom(original);
          }

          // Pattern variable from a prior &&-chain conjunct: replace with the pre-declared
          // outer variable (TypedReplacement is e.g. "((User)__pv0_u!)").
          if (_bindings != null
              && symbol is ILocalSymbol local
              && !IsDeclaredWithin(local, _fragment)
              && _bindings.TryGetValue(local.Name, out var localBinding)
              && localBinding.TypedReplacement != null)
          {
            return SyntaxFactory.ParseExpression(localBinding.TypedReplacement).WithTriviaFrom(original);
          }

          return visited;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
          var symbol = _model.GetSymbolInfo(node, _ct).Symbol as IMethodSymbol;
          var visited = (InvocationExpressionSyntax?)base.VisitInvocationExpression(node);

          if (symbol is not { ReducedFrom: not null } || visited == null)
          {
            return visited;
          }

          if (visited.Expression is not MemberAccessExpressionSyntax access
              || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
          {
            Failed = true;
            return visited;
          }

          var staticClass = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          var typeArguments = access.Name is GenericNameSyntax generic ? generic.TypeArgumentList.ToString() : "";
          var arguments = new List<string> { access.Expression.ToString() };
          arguments.AddRange(visited.ArgumentList.Arguments.Select(a => a.ToString()));

          return SyntaxFactory.ParseExpression($"{staticClass}.{symbol.Name}{typeArguments}({string.Join(", ", arguments)})")
            .WithTriviaFrom(visited);
        }
      }

      /// <summary>
      /// Compiles an operand to an object-typed expression that evaluates it through the
      /// closure and runtime reflection. Supported shapes: literals, captured locals and
      /// parameters (of any type), `this` and its fields/properties/method calls (any
      /// accessibility), member access chains, parameterless LINQ Count(), and static
      /// fields/properties including enum constants on types reachable through a nameable
      /// ancestor. Anything else fails.
      /// </summary>
      private string? CompileReflective(ExpressionSyntax expression, List<(string Name, string? Type)> captures,
        IReadOnlyDictionary<string, LambdaBinding>? bindings = null)
      {
        expression = StripParens(expression);

        switch (expression)
        {
          case LiteralExpressionSyntax literal:
            return $"(object)({literal})";

          case ThisExpressionSyntax:
            return CapturedThis;

          case AwaitExpressionSyntax awaitExpression:
          {
            // Only reachable in async render callbacks: synchronous embeddings (exception
            // steps, custom-pattern probes) exclude await fragments via ContainsAwait.
            if (CompileReflective(awaitExpression.Expression, captures, bindings) is not { } awaited)
            {
              return null;
            }

            // A nameable awaitable type gets a typed await (cast back from object): no
            // reflection, so it also survives Native AOT trimming. AwaitResult (reflective
            // Task.Result read) is the fallback for unnameable awaitable types.
            var awaitableType = _model.GetTypeInfo(awaitExpression.Expression, _ct).Type;

            if (awaitableType != null && IsUsableType(awaitableType, _compilation))
            {
              var awaitableFqn = awaitableType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
              return $"((object)(await (({awaitableFqn})({awaited}))))";
            }

            return $"(await {Runtime}.AwaitResult({awaited}))";
          }

          case IdentifierNameSyntax identifier:
            switch (_model.GetSymbolInfo(identifier, _ct).Symbol)
            {
              case ILocalSymbol { IsConst: false } local:
                // Pattern variable binding from a prior &&-chain conjunct.
                if (bindings != null && bindings.TryGetValue(local.Name, out var pvBinding))
                {
                  return pvBinding.ReflectiveReplacement;
                }

                // Local declared inside the body but not bound = pattern var from another
                // conjunct without a binding → not in the closure, cannot read reflectively.
                if (IsDeclaredWithin(local, _body))
                {
                  return null;
                }

                AddCapture(captures, local.Name, local.Type);
                return Identifier(local.Name);

              case IParameterSymbol param when IsDeclaredWithin(param, _body):
                return bindings != null && bindings.TryGetValue(param.Name, out var binding)
                  ? binding.ReflectiveReplacement
                  : null;

              case IParameterSymbol param:
                AddCapture(captures, param.Name, param.Type);
                return Identifier(param.Name);

              case IFieldSymbol { IsStatic: false } field:
                return $"{Runtime}.GetMemberValue({CapturedThis}, {Quote(MetadataName(field))})";

              case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                return $"{Runtime}.GetMemberValue({CapturedThis}, {Quote(property.Name)})";

              case IFieldSymbol { IsStatic: true } staticField:
                return StaticMember(staticField.ContainingType, MetadataName(staticField));

              case IPropertySymbol { IsStatic: true } staticProperty:
                return StaticMember(staticProperty.ContainingType, staticProperty.Name);

              default:
                return null;
            }

          case MemberAccessExpressionSyntax memberAccess when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
            switch (_model.GetSymbolInfo(memberAccess, _ct).Symbol)
            {
              case IFieldSymbol { IsStatic: true } staticField:
                // Includes enum constants like MyEnum.B on a private nested enum.
                return StaticMember(staticField.ContainingType, MetadataName(staticField));

              case IPropertySymbol { IsStatic: true } staticProperty:
                return StaticMember(staticProperty.ContainingType, staticProperty.Name);

              case IFieldSymbol { IsStatic: false } field:
                return CompileReflective(memberAccess.Expression, captures, bindings) is { } fieldReceiver
                  ? $"{Runtime}.GetMemberValue({fieldReceiver}, {Quote(MetadataName(field))})"
                  : null;

              case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                return CompileReflective(memberAccess.Expression, captures, bindings) is { } propertyReceiver
                  ? $"{Runtime}.GetMemberValue({propertyReceiver}, {Quote(property.Name)})"
                  : null;

              default:
                return null;
            }

          case ElementAccessExpressionSyntax elementAccess when elementAccess.ArgumentList.Arguments.Count == 1:
          {
            var elementReceiver = CompileReflective(elementAccess.Expression, captures, bindings);
            var elementIndex = elementReceiver != null
              ? CompileReflective(elementAccess.ArgumentList.Arguments[0].Expression, captures, bindings)
              : null;

            return elementIndex != null
              ? $"{Runtime}.GetElementValue({elementReceiver}, {elementIndex})"
              : null;
          }

          case BinaryExpressionSyntax binary when IsReflectiveOperatorKind(binary.Kind()):
          {
            // Operators need typed operands: each side is compiled reflectively and cast
            // back to its (nameable) static type, preserving the source semantics.
            var leftType = _model.GetTypeInfo(binary.Left, _ct).Type;
            var rightType = _model.GetTypeInfo(binary.Right, _ct).Type;

            if (leftType == null || rightType == null
                || !IsUsableType(leftType, _compilation) || !IsUsableType(rightType, _compilation))
            {
              return null;
            }

            var leftCompiled = CompileReflective(binary.Left, captures, bindings);
            var rightCompiled = leftCompiled != null ? CompileReflective(binary.Right, captures, bindings) : null;

            if (rightCompiled == null)
            {
              return null;
            }

            var leftFqn = leftType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var rightFqn = rightType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return $"((({leftFqn})({leftCompiled})) {binary.OperatorToken.Text} (({rightFqn})({rightCompiled})))";
          }

          case TupleExpressionSyntax tuple:
          {
            // Tuple literals are reconstructed element-wise: each element compiles
            // reflectively and is cast back to its static type, producing a real
            // ValueTuple without reflecting on the tuple itself (AOT-safe). The natural
            // type is what the compiled fragment boxes (casting to a converted type
            // would unbox-mismatch); ConvertedType only serves typeless elements
            // (null/default), where the cast is a reference conversion anyway.
            var elements = new List<string>();

            foreach (var argument in tuple.Arguments)
            {
              var elementInfo = _model.GetTypeInfo(argument.Expression, _ct);
              var elementType = elementInfo.Type ?? elementInfo.ConvertedType;

              if (elementType == null || !IsUsableType(elementType, _compilation)
                  || CompileReflective(argument.Expression, captures, bindings) is not { } element)
              {
                return null;
              }

              var elementFqn = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
              elements.Add($"(({elementFqn})({element}))");
            }

            return $"((object)(({string.Join(", ", elements)})))";
          }

          case InvocationExpressionSyntax invocation:
          {
            if (_model.GetSymbolInfo(invocation, _ct).Symbol is not IMethodSymbol method)
            {
              return null;
            }

            // Local functions are not members of their containing type — not resolvable
            // reflectively — but a lifted copy of the declaration can be called directly.
            if (method.MethodKind == MethodKind.LocalFunction)
            {
              return CompileLiftedLocalFunctionCall(invocation, method, captures, bindings);
            }

            // LINQ Count() works untyped: the element type never needs to be named.
            if (method is { ReducedFrom: not null, Parameters.Length: 0, Name: "Count", ContainingType.Name: "Enumerable" }
                && method.ContainingType.ContainingNamespace is { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
                && invocation.Expression is MemberAccessExpressionSyntax countAccess
                && countAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
              return CompileReflective(countAccess.Expression, captures, bindings) is { } countReceiver
                ? $"{Runtime}.EnumerableCount({countReceiver})"
                : null;
            }

            // LINQ Count(predicate) over an unnameable element type: the predicate is
            // compiled with its parameter bound reflectively.
            if (method is { ReducedFrom: not null, Parameters.Length: 1, Name: "Count", ContainingType.Name: "Enumerable" }
                && method.ContainingType.ContainingNamespace is { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
                && CompileReflectiveStaticCall(invocation, method, captures, bindings) is null
                && invocation.Expression is MemberAccessExpressionSyntax filteredCountAccess
                && filteredCountAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                && invocation.ArgumentList.Arguments.Count == 1
                && invocation.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax { Body: ExpressionSyntax filterBody } filterLambda
                && _model.GetDeclaredSymbol(filterLambda.Parameter, _ct) is { } filterParameter)
            {
              var filterBindings = bindings != null
                ? new Dictionary<string, LambdaBinding>(System.Linq.Enumerable.ToDictionary(bindings, kv => kv.Key, kv => kv.Value))
                : new Dictionary<string, LambdaBinding>();

              filterBindings[filterParameter.Name] = new LambdaBinding(
                IsUsableType(filterParameter.Type, _compilation)
                  ? $"(({filterParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})__k)"
                  : null,
                "__k");

              var countReceiver = CompileReflective(filteredCountAccess.Expression, captures, bindings);
              var countFilter = countReceiver != null ? Compile(filterBody, filterBindings) : null;

              return countFilter != null
                ? $"{Runtime}.EnumerableCount({countReceiver}, (object __k) => (bool)(object)({countFilter}))"
                : null;
            }

            if (method.IsStatic || method.ReducedFrom != null)
            {
              return CompileReflectiveStaticCall(invocation, method, captures, bindings)
                     ?? CompileReflectiveLinqCall(invocation, method, captures, bindings)
                     ?? CompileReflectiveUntypedStaticCall(invocation, method, captures, bindings);
            }

            if (method.IsGenericMethod
                || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
                || !HasSingleOverload(method))
            {
              return null;
            }

            var instanceArgExprs = GetArgumentExpressionsInParameterOrder(
              invocation.ArgumentList.Arguments, method.Parameters);
            if (instanceArgExprs == null) return null;

            var receiver = invocation.Expression switch
            {
              MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                => CompileReflective(access.Expression, captures, bindings),
              IdentifierNameSyntax => CapturedThis, // implicit this
              _ => null,
            };

            if (receiver == null)
            {
              return null;
            }

            var arguments = new List<string>();

            foreach (var argExpr in instanceArgExprs)
            {
              if (CompileReflective(argExpr, captures, bindings) is not { } compiled)
              {
                return null;
              }

              arguments.Add(compiled);
            }

            var argumentArray = arguments.Count == 0
              ? "[]"
              : $"[{string.Join(", ", arguments)}]";

            return $"{Runtime}.InvokeInstance({receiver}, {Quote(method.Name)}, {argumentArray})";
          }

          default:
            return null;
        }
      }

      /// <summary>
      /// A static or (reduced) extension call whose full signature is nameable: emitted as
      /// a typed static call with explicit type arguments, each non-lambda argument compiled
      /// reflectively and cast to its parameter type, lambda-literal arguments pasted typed.
      /// This bridges reflective receivers (private element types upstream) into typed
      /// static calls (int.Parse, Enumerable.Where, ...).
      /// </summary>
      private string? CompileReflectiveStaticCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
        List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        var constructed = method.ReducedFrom != null ? method.GetConstructedReducedFrom() : method;

        if (constructed == null
            || !IsAccessibleMember(method)
            || !IsUsableType(method.ContainingType, _compilation)
            || constructed.TypeArguments.Any(t => !IsUsableType(t, _compilation))
            || constructed.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams))
        {
          return null;
        }

        var syntaxArguments = new List<ExpressionSyntax>();

        if (method.ReducedFrom != null)
        {
          if (invocation.Expression is not MemberAccessExpressionSyntax access
              || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
          {
            return null;
          }

          syntaxArguments.Add(access.Expression);
        }

        // Sort named arguments to parameter positions; positional args pass through unchanged.
        // For reduced extension methods, `constructed.Parameters[0]` is the receiver (already
        // added above from access.Expression), so only match against the remaining parameters.
        var callParameters = method.ReducedFrom != null
          ? constructed.Parameters.RemoveAt(0)
          : constructed.Parameters;
        var sortedArgs = GetArgumentExpressionsInParameterOrder(
          invocation.ArgumentList.Arguments, callParameters);
        if (sortedArgs == null) return null;
        syntaxArguments.AddRange(sortedArgs);

        if (syntaxArguments.Count != constructed.Parameters.Length)
        {
          return null; // omitted optional arguments: not worth reconstructing
        }

        var rendered = new List<string>();

        for (var i = 0; i < syntaxArguments.Count; i++)
        {
          var parameterType = constructed.Parameters[i].Type;

          if (!IsUsableType(parameterType, _compilation))
          {
            return null;
          }

          var parameterTypeFqn = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

          if (syntaxArguments[i] is AnonymousFunctionExpressionSyntax)
          {
            // Lambdas can't be object-typed; paste them typed (their parameters bind inside).
            if (CompileTyped(syntaxArguments[i], bindings) is not { } typedLambda)
            {
              return null;
            }

            rendered.Add($"({parameterTypeFqn})({typedLambda})");
            continue;
          }

          if (CompileReflective(syntaxArguments[i], captures, bindings) is not { } compiled)
          {
            return null;
          }

          rendered.Add($"(({parameterTypeFqn})({compiled}))");
        }

        var typeArguments = constructed.TypeArguments.Length > 0
          ? $"<{string.Join(", ", constructed.TypeArguments.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))}>"
          : "";

        var containingType = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return $"{containingType}.{method.Name}{typeArguments}({string.Join(", ", rendered)})";
      }

      /// <summary>
      /// A local-function call inside an otherwise reflective fragment: the declaration is
      /// lifted into the generated scope and called directly, each object-typed argument
      /// cast back to its parameter type. Generic local functions stay typed-path-only.
      /// </summary>
      private string? CompileLiftedLocalFunctionCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
        List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        if (method.IsGenericMethod
            || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
            || invocation.ArgumentList.Arguments.Count != method.Parameters.Length
            || invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
            || method.Parameters.Any(p => !IsUsableType(p.Type, _compilation))
            || !TryLiftLocalFunction(method))
        {
          return null;
        }

        var rendered = new List<string>();

        for (var i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
          if (CompileReflective(invocation.ArgumentList.Arguments[i].Expression, captures, bindings) is not { } compiled)
          {
            return null;
          }

          var parameterTypeFqn = method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          rendered.Add($"(({parameterTypeFqn})({compiled}))");
        }

        return $"{Identifier(method.Name)}({string.Join(", ", rendered)})";
      }

      /// <summary>
      /// A static call the typed strategies rejected (private helper methods, unnameable
      /// parameter types): the containing type is anchored via TypeAccessor and the method
      /// resolved by name + argument count at runtime — the static counterpart of the
      /// InvokeInstance path for private instance members. Reduced extension calls and
      /// generic methods stay out (type arguments cannot be reconstructed reflectively).
      /// </summary>
      private string? CompileReflectiveUntypedStaticCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
        List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        if (method.ReducedFrom != null
            || method.IsGenericMethod
            || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
            || !HasSingleStaticOverload(method)
            || TypeAccessor(method.ContainingType) is not { } typeAccessor)
        {
          return null;
        }

        var untypedArgExprs = GetArgumentExpressionsInParameterOrder(
          invocation.ArgumentList.Arguments, method.Parameters);
        if (untypedArgExprs == null) return null;

        var arguments = new List<string>();

        foreach (var argExpr in untypedArgExprs)
        {
          if (CompileReflective(argExpr, captures, bindings) is not { } compiled)
          {
            return null;
          }

          arguments.Add(compiled);
        }

        var argumentArray = arguments.Count == 0
          ? "[]"
          : $"[{string.Join(", ", arguments)}]";

        return $"{Runtime}.InvokeStatic({typeAccessor}, {Quote(method.Name)}, {argumentArray})";
      }

      /// <summary>
      /// Returns argument expressions sorted to parameter order, resolving named arguments.
      /// Returns null if any named label doesn't match a parameter or if there's a conflict.
      /// </summary>
      private static List<ExpressionSyntax>? GetArgumentExpressionsInParameterOrder(
        SeparatedSyntaxList<ArgumentSyntax> args, System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters)
      {
        if (args.Count != parameters.Length) return null;

        if (!args.Any(a => a.NameColon != null))
          return args.Select(a => a.Expression).ToList();

        var result = new ExpressionSyntax?[parameters.Length];

        for (var i = 0; i < args.Count; i++)
        {
          var arg = args[i];

          if (arg.NameColon != null)
          {
            var label = arg.NameColon.Name.Identifier.ValueText;
            var paramIdx = -1;
            for (var j = 0; j < parameters.Length; j++)
              if (parameters[j].Name == label) { paramIdx = j; break; }
            if (paramIdx < 0 || result[paramIdx] != null) return null;
            result[paramIdx] = arg.Expression;
          }
          else
          {
            if (result[i] != null) return null;
            result[i] = arg.Expression;
          }
        }

        return result.All(r => r != null) ? result.Select(r => r!).ToList() : null;
      }

      /// <summary>
      /// Runtime resolution is by name + parameter count: the combination must be
      /// unambiguous across the hierarchy InvokeStatic searches.
      /// </summary>
      private static bool HasSingleStaticOverload(IMethodSymbol method)
      {
        var count = 0;

        for (var type = method.ContainingType; type != null; type = type.BaseType)
        {
          foreach (var member in type.GetMembers(method.Name))
          {
            if (member is IMethodSymbol { IsStatic: true } candidate
                && candidate.Parameters.Length == method.Parameters.Length)
            {
              count++;
            }
          }
        }

        return count == 1;
      }

      /// <summary>
      /// Parameterless LINQ extension calls (First, Single, ...) over collections whose
      /// element type cannot be named: resolved at runtime via reflection.
      /// </summary>
      private string? CompileReflectiveLinqCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
        List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, LambdaBinding>? bindings)
      {
        if (method is not { ReducedFrom: not null, Parameters.Length: 0, ContainingType.Name: "Enumerable" }
            || method.ContainingType.ContainingNamespace is not { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
            || invocation.Expression is not MemberAccessExpressionSyntax access
            || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
          return null;
        }

        return CompileReflective(access.Expression, captures, bindings) is { } source
          ? $"{Runtime}.InvokeLinq({source}, {Quote(method.Name)})"
          : null;
      }

      /// <summary>
      /// A typeof()/GetNestedType() chain for a (possibly unnameable) type: the outermost
      /// nameable ancestor anchors a typeof(), and each unnameable nesting level is resolved
      /// by metadata name at runtime. Null when no nameable anchor exists.
      /// </summary>
      public string? TypeAccessor(ITypeSymbol type)
      {
        if (IsUsableType(type, _compilation))
        {
          return $"typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
        }

        if (type is not INamedTypeSymbol { IsGenericType: false, IsFileLocal: false, ContainingType: { } parent })
        {
          return null;
        }

        return TypeAccessor(parent) is { } parentAccessor
          ? $"{Runtime}.GetNestedType({parentAccessor}, {Quote(type.MetadataName)})"
          : null;
      }

      private string? StaticMember(INamedTypeSymbol containingType, string memberName)
      {
        return TypeAccessor(containingType) is { } typeAccessor
          ? $"{Runtime}.GetStaticMemberValue({typeAccessor}, {Quote(memberName)})"
          : null;
      }

      private static bool IsReflectiveOperatorKind(SyntaxKind kind)
      {
        return kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
          or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
          or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression
          or SyntaxKind.AddExpression or SyntaxKind.SubtractExpression
          or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression;
      }

      /// <summary>
      /// Runtime resolution is by name + parameter count, so interception requires that
      /// combination to be unambiguous across the type hierarchy.
      /// </summary>
      private static bool HasSingleOverload(IMethodSymbol method)
      {
        var count = 0;

        for (var type = method.ContainingType; type != null; type = type.BaseType)
        {
          foreach (var member in type.GetMembers(method.Name))
          {
            if (member is IMethodSymbol { IsStatic: false } candidate
                && candidate.Parameters.Length == method.Parameters.Length
                && candidate.OverriddenMethod == null)
            {
              count++;
            }
          }
        }

        return count == 1;
      }

      /// <summary>Tuple elements are read through their underlying ItemN field.</summary>
      private static string MetadataName(IFieldSymbol field)
      {
        return (field.CorrespondingTupleField ?? field).Name;
      }

      private void AddCapture(List<(string Name, string? Type)> captures, string name, ITypeSymbol type)
      {
        var typeFqn = IsUsableType(type, _compilation)
          ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          : null;

        captures.Add((name, typeFqn));
      }

      private void Commit(List<(string Name, string? Type)> captures)
      {
        foreach (var capture in captures)
        {
          var existing = _call.CapturedLocals.FindIndex(l => l.Name == capture.Name);

          if (existing < 0)
          {
            _call.CapturedLocals.Add(capture);
          }
          else if (_call.CapturedLocals[existing].Type == null && capture.Type != null)
          {
            _call.CapturedLocals[existing] = capture;
          }
        }
      }

      private static string Quote(string text) => SymbolDisplay.FormatLiteral(text, quote: true);
    }

    /// <summary>
    /// Expands implicit anonymous-object member declarators for display
    /// (`new { n, i }` to `new { n = n, i = i }`), matching the old expression-tree
    /// rendering of anonymous types.
    /// </summary>
    private sealed class AnonymousMemberExpander : CSharpSyntaxRewriter
    {
      public override SyntaxNode? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
      {
        if (node.NameEquals != null)
        {
          return base.VisitAnonymousObjectMemberDeclarator(node);
        }

        var name = node.Expression switch
        {
          IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
          MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
          _ => null,
        };

        if (name == null)
        {
          return base.VisitAnonymousObjectMemberDeclarator(node);
        }

        var nameEquals = SyntaxFactory.NameEquals(
          SyntaxFactory.IdentifierName(name),
          SyntaxFactory.Token(SyntaxKind.EqualsToken)
            .WithLeadingTrivia(SyntaxFactory.Space)
            .WithTrailingTrivia(SyntaxFactory.Space));

        return node
          .WithNameEquals(nameEquals.WithLeadingTrivia(node.Expression.GetLeadingTrivia()))
          .WithExpression(node.Expression.WithoutLeadingTrivia());
      }
    }

    /// <summary>
    /// Renames an All() filter's lambda parameter to "item" for display purposes
    /// (the bound-item naming of the old AllPattern's per-item sub-analysis).
    /// </summary>
    private sealed class ParamRenameRewriter : CSharpSyntaxRewriter
    {
      private readonly SemanticModel _model;
      private readonly IParameterSymbol _parameter;
      private readonly CancellationToken _ct;

      public ParamRenameRewriter(SemanticModel model, IParameterSymbol parameter, CancellationToken ct)
      {
        _model = model;
        _parameter = parameter;
        _ct = ct;
      }

      public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
      {
        if (node.Identifier.ValueText == _parameter.Name
            && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(node, _ct).Symbol, _parameter))
        {
          return SyntaxFactory.IdentifierName("item").WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
      }
    }

    private static bool HasAssertionWrapperAttribute(IMethodSymbol method)
    {
      foreach (var attribute in method.GetAttributes())
      {
        if (attribute.AttributeClass is { Name: "AssertionWrapperAttribute", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } })
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Validates an [AssertionWrapper] method and locates its paired AssertionHandle
    /// overload: same name, same staticness, same parameters except the first, and
    /// accessible from generated code (which lives outside the declaring type).
    /// </summary>
    private static WrapperModel? AnalyzeWrapper(IMethodSymbol method, Compilation compilation)
    {
      if (method.IsGenericMethod
          || method.ContainingType is not { IsGenericType: false, IsFileLocal: false } containingType
          || !compilation.IsSymbolAccessibleWithin(containingType, compilation.Assembly))
      {
        return null;
      }

      foreach (var parameter in method.Parameters)
      {
        if (parameter.RefKind != RefKind.None || parameter.IsParams)
        {
          return null;
        }
      }

      for (var i = 1; i < method.Parameters.Length; i++)
      {
        if (!IsUsableType(method.Parameters[i].Type, compilation))
        {
          return null;
        }
      }

      if (!method.ReturnsVoid && !IsUsableType(method.ReturnType, compilation))
      {
        return null;
      }

      IMethodSymbol? handleOverload = null;

      foreach (var member in containingType.GetMembers(method.Name))
      {
        if (member is IMethodSymbol candidate
            && !SymbolEqualityComparer.Default.Equals(candidate, method)
            && candidate.IsStatic == method.IsStatic
            && !candidate.IsGenericMethod
            && candidate.Parameters.Length == method.Parameters.Length
            && candidate.Parameters[0].Type is INamedTypeSymbol { Name: "AssertionHandle", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } }
            && RemainingParametersMatch(candidate, method)
            && compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly))
        {
          handleOverload = candidate;
          break;
        }
      }

      if (handleOverload == null)
      {
        return null;
      }

      var model = new WrapperModel
      {
        MethodName = method.Name,
        ContainingTypeFqn = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        IsStatic = method.IsStatic,
        ReturnTypeFqn = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
      };

      for (var i = 1; i < method.Parameters.Length; i++)
      {
        model.ExtraParameterTypes.Add(method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
      }

      return model;
    }

    private static bool RemainingParametersMatch(IMethodSymbol candidate, IMethodSymbol original)
    {
      for (var i = 1; i < original.Parameters.Length; i++)
      {
        if (!SymbolEqualityComparer.Default.Equals(candidate.Parameters[i].Type, original.Parameters[i].Type)
            || candidate.Parameters[i].RefKind != RefKind.None)
        {
          return false;
        }
      }

      return true;
    }

    private static ThatOverload? ClassifyOverload(IMethodSymbol method)
    {
      // That(Func<bool>, object? message, Func<object?>? context)
      // and That(Func<bool>, Func<object?> context).
      return method.Parameters.Length switch
      {
        2 when method.Parameters[1].Type is INamedTypeSymbol { Name: "Func" } => ThatOverload.Context,
        3 when method.Parameters[1].Type.SpecialType == SpecialType.System_Object => ThatOverload.MessageContext,
        _ => null,
      };
    }

    private static bool IsDeclaredWithin(ISymbol symbol, SyntaxNode scope)
    {
      foreach (var reference in symbol.DeclaringSyntaxReferences)
      {
        if (reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span))
        {
          return true;
        }
      }

      return false;
    }

    private static bool IsLinqEnumerableMethod(IMethodSymbol method)
    {
      return method is { ReducedFrom: not null, ContainingType: { Name: "Enumerable", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } };
    }

    private static bool IsMemoryExtensionsMethod(IMethodSymbol method)
    {
      return method is { ReducedFrom: not null, ContainingType: { Name: "MemoryExtensions", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };
    }

    private static bool IsNullableValueType(ITypeSymbol? type)
    {
      return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    /// <summary>
    /// Whether the type can be written down (for the closure-value cast) and used from the
    /// generated file, which lives in the consumer assembly but outside any type/file scope.
    /// </summary>
    private static bool IsUsableType(ITypeSymbol type, Compilation compilation)
    {
      switch (type)
      {
        case ITypeParameterSymbol:
          return false;
        case IPointerTypeSymbol:
        case IFunctionPointerTypeSymbol:
          return false;
        case IArrayTypeSymbol array:
          return IsUsableType(array.ElementType, compilation);
        case INamedTypeSymbol named:
          if (named.IsAnonymousType || named.IsFileLocal || named.IsRefLikeType || named.TypeKind == TypeKind.Dynamic)
          {
            return false;
          }

          if (!compilation.IsSymbolAccessibleWithin(named, compilation.Assembly))
          {
            return false;
          }

          foreach (var typeArgument in named.TypeArguments)
          {
            if (!IsUsableType(typeArgument, compilation))
            {
              return false;
            }
          }

          return true;
        default:
          return false;
      }
    }

    private static bool IsNullOrDefaultLiteral(ExpressionSyntax expression)
    {
      return expression.IsKind(SyntaxKind.NullLiteralExpression)
             || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
             || expression is DefaultExpressionSyntax;
    }

    private static bool IsLengthOrCountAccess(ExpressionSyntax expression)
    {
      return expression switch
      {
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" } => true,
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "LongCount" } } => true,
        _ => false,
      };
    }

    private static ExpressionSyntax StripParens(ExpressionSyntax expression)
    {
      while (expression is ParenthesizedExpressionSyntax p)
      {
        expression = p.Expression;
      }

      return expression;
    }
  }
}
