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

      if (!ClassifyForm(new ClassificationContext(ctx, call, compiler, ct), core, outerNegated))
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

      if (!ClassifyForm(new ClassificationContext(ctx, subCall, compiler, ct, bindings), subCore, subNegated))
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
    internal static bool IsSplittableLogical(BinaryExpressionSyntax binary, GeneratorSyntaxContext ctx, CancellationToken ct)
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
    internal static bool IsPureAndAlsoSyntax(ExpressionSyntax expression)
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
    internal static SplitPart? BuildSplitPart(GeneratorSyntaxContext ctx, ExpressionSyntax expression,
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
      var classified = ClassifyForm(new ClassificationContext(ctx, subCall, compiler, ct, patternBindings), leafCore, leafNegated, leafContext: true);

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

    internal static (string Label, string Op) GetRelationalParts(SyntaxKind kind) => kind switch
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

    private static readonly ILeafClassifier[] s_classifiers =
    {
      new LogicalSplitClassifier(),
      new EqualityClassifier(),
      new ComparisonClassifier(),
      new IsExpressionClassifier(),
      new IsPatternClassifier(),
      new MethodCallClassifier(),
      new HasValueClassifier(),
      new BoolMemberClassifier(),
    };

    /// <summary>
    /// Determines which pattern the (negation-stripped) assertion body matches, compiles
    /// the operands the pattern needs, and fills the pattern-specific fields of the call.
    /// Returns false when the body is not a whitelisted form or an operand can't be compiled.
    /// When classifying an All() filter body as a per-item sub-assertion, `bindings` maps
    /// the lambda parameter to its bound value and `display` rewrites it to "item" in
    /// displayed sources (AllPattern's NamedConstantExpression equivalent).
    /// </summary>
    internal static bool ClassifyForm(ClassificationContext ctx, ExpressionSyntax core, bool outerNegated, bool leafContext = false)
    {
      foreach (var classifier in s_classifiers)
      {
        var result = classifier.TryClassify(ctx, core, outerNegated, leafContext);
        if (result.HasValue)
          return result.Value;
      }
      return false;
    }

    internal static bool SetLeft(InterceptedCall call, OperandCompiler compiler, ExpressionSyntax operand,
      IReadOnlyDictionary<string, LambdaBinding>? bindings = null, System.Func<ExpressionSyntax, string>? display = null)
    {
      call.LeftSource = compiler.Compile(operand, bindings)!;
      call.LeftDisplay = display?.Invoke(operand) ?? operand.ToString();
      return call.LeftSource != null;
    }

    internal static bool SetRight(InterceptedCall call, OperandCompiler compiler, ExpressionSyntax operand, ExpressionSyntax stripped,
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
    internal static bool TryGetLengthAccess(ExpressionSyntax left, out string countLabel, out ExpressionSyntax operand, out string? filterSource)
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

    internal static bool IsLinqEnumerableMethod(IMethodSymbol method)
    {
      return method is { ReducedFrom: not null, ContainingType: { Name: "Enumerable", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } };
    }

    internal static bool IsMemoryExtensionsMethod(IMethodSymbol method)
    {
      return method is { ReducedFrom: not null, ContainingType: { Name: "MemoryExtensions", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };
    }

    internal static bool IsNullableValueType(ITypeSymbol? type)
    {
      return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    /// <summary>
    /// Whether the type can be written down (for the closure-value cast) and used from the
    /// generated file, which lives in the consumer assembly but outside any type/file scope.
    /// </summary>
    internal static bool IsUsableType(ITypeSymbol type, Compilation compilation)
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

    internal static bool IsNullOrDefaultLiteral(ExpressionSyntax expression)
    {
      return expression.IsKind(SyntaxKind.NullLiteralExpression)
             || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
             || expression is DefaultExpressionSyntax;
    }

    internal static bool IsLengthOrCountAccess(ExpressionSyntax expression)
    {
      return expression switch
      {
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" } => true,
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "LongCount" } } => true,
        _ => false,
      };
    }

    internal static ExpressionSyntax StripParens(ExpressionSyntax expression)
    {
      while (expression is ParenthesizedExpressionSyntax p)
      {
        expression = p.Expression;
      }

      return expression;
    }
  }
}
