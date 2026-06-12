using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  /// <summary>
  /// Builds SplitPart trees for logically-composed and pattern-matching assertion expressions.
  /// Each leaf carries a re-evaluation of its source, a classified decomposition, a custom-pattern
  /// probe, exception steps, and the captured locals it introduced.
  /// </summary>
  internal static class SplitTreeBuilder
  {
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
      expression = CallSiteAnalyzer.StripParens(expression);

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
      Dictionary<string, CallSiteAnalyzer.LambdaBinding>? patternBindings = null)
    {
      expression = CallSiteAnalyzer.StripParens(expression);

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
          && CallSiteAnalyzer.IsUsableType(pvSymbol.Type, ctx.SemanticModel.Compilation))
      {
        var typeFqn = pvSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var origName = pvDesig.Identifier.ValueText;
        var pvIndex = patternBindings.Count;
        var preDeclName = $"__pv{pvIndex}_{origName}";
        var tmpName = $"__pvtmp{pvIndex}_{origName}";

        designationRenames = new Dictionary<string, string> { [origName] = tmpName };

        // The TypedReplacement cast peels the nullable and casts to the declared type.
        patternBindings[origName] = new CallSiteAnalyzer.LambdaBinding($"(({typeFqn}){preDeclName}!)", preDeclName);
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
              && CallSiteAnalyzer.IsUsableType(outLocal.Type, ctx.SemanticModel.Compilation))
          {
            var typeFqn = outLocal.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var origName = outDesig.Identifier.ValueText;
            var pvIdx = patternBindings.Count;
            var preDeclName = $"__pv{pvIdx}_{origName}";
            var tmpName = $"__pvtmp{pvIdx}_{origName}";

            (designationRenames ??= new Dictionary<string, string>())[origName] = tmpName;
            patternBindings[origName] = new CallSiteAnalyzer.LambdaBinding($"(({typeFqn}){preDeclName}!)", preDeclName);
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
        leafCore = CallSiteAnalyzer.StripParens(negation.Operand);
      }

      // Kinds that don't recompute negation (Contains, Bool, ...) read the pre-set value.
      var subCall = new InterceptedCall { Negated = leafNegated };
      var classified = CallSiteAnalyzer.ClassifyForm(new ClassificationContext(ctx, subCall, compiler, ct, patternBindings), leafCore, leafNegated, leafContext: true);

      var part = new SplitPart
      {
        LeafSource = expression.ToString(),
        ConditionSource = condition,
        SubCall = classified ? subCall : null,
        StepsSource = new CallSiteAnalyzer.ExceptionStepWalker(ctx.SemanticModel, compiler, expression, ct).CollectSource(expression),
        ProbeSource = CallSiteAnalyzer.BuildCustomProbe(ctx, leafCore, leafNegated, compiler, ct),
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
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
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
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
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
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
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

        case ConstantPatternSyntax nullConst when CallSiteAnalyzer.IsNullOrDefaultLiteral(nullConst.Expression):
        {
          var leftType = ctx.SemanticModel.GetTypeInfo(subject, ct).Type;
          if (leftType is not { IsReferenceType: true } && !CallSiteAnalyzer.IsNullableValueType(leftType)) return null;
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

        case ConstantPatternSyntax otherConst when !CallSiteAnalyzer.IsNullOrDefaultLiteral(CallSiteAnalyzer.StripParens(subject)):
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
      Dictionary<string, CallSiteAnalyzer.LambdaBinding>? patternBindings)
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
        if (checkedType == null || !CallSiteAnalyzer.IsUsableType(checkedType, ctx.SemanticModel.Compilation)) return null;

        var typeAccessor = compiler.TypeAccessor(checkedType);
        if (typeAccessor == null) return null;

        var typeFqn = checkedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Pre-declare a cast variable via PatternVars so it lives outside the try block
        // and subsequent property leaves can reference it.
        patternBindings ??= new Dictionary<string, CallSiteAnalyzer.LambdaBinding>();
        var pvIndex = patternBindings.Count;
        var castPreDecl = $"__pv{pvIndex}_cast";
        var castTmp = $"__pvtmp{pvIndex}_cast";
        patternBindings[castPreDecl] = new CallSiteAnalyzer.LambdaBinding($"(({typeFqn}){castPreDecl}!)", castPreDecl);

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
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
    {
      var negated = false;
      while (pattern is UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern } notPat)
      {
        negated = !negated;
        pattern = notPat.Pattern;
      }

      switch (pattern)
      {
        case ConstantPatternSyntax nullConst when CallSiteAnalyzer.IsNullOrDefaultLiteral(nullConst.Expression):
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
      Dictionary<string, CallSiteAnalyzer.LambdaBinding>? patternBindings)
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
          if (!CallSiteAnalyzer.IsUsableType(elemLocal.Type, ctx.SemanticModel.Compilation)) continue;

          // Strip outer Nullable<T> so the emitter's appended ? doesn't produce T??.
          var elemType = elemLocal.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableT
            ? nullableT.TypeArguments[0]
            : elemLocal.Type;
          var typeFqn = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

          var pvIndex = patternBindings.Count;
          var preDeclName = $"__pv{pvIndex}_{origName}";
          var tmpName = $"__pvtmp{pvIndex}_{origName}";

          patternBindings[origName] = new CallSiteAnalyzer.LambdaBinding($"(({typeFqn}){preDeclName}!)", preDeclName);

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

    internal static string Quote(string text) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, quote: true);
  }
}
