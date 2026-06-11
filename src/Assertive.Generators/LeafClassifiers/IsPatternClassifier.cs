using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class IsPatternClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      if (expr is not IsPatternExpressionSyntax isPattern)
        return null;

      // C# 7+ pattern matching: `obj is Type`, `obj is Type u`, `obj is null`, `n is > 18`,
      // `obj is User { Name: "Bob" }`, `n is >= 0 and <= 100`, `obj is not null`, etc.

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
      if (!patternNegated && !leafContext && !outerNegated && ctx.Bindings == null
          && innerPat is BinaryPatternSyntax andBin && andBin.IsKind(SyntaxKind.AndPattern))
      {
        ctx.Call.Kind = InterceptionKind.Split;
        var innerBindings = new Dictionary<string, CallSiteAnalyzer.LambdaBinding>();
        ctx.Call.SplitRoot = CallSiteAnalyzer.BuildSplitPart(ctx.Syntax, isPattern, ctx.Call, ctx.Compiler, ctx.Ct, innerBindings);
        return ctx.Call.SplitRoot != null;
      }

      // Property pattern (obj is User { Name: "Bob" } or just { Name: "Bob" }):
      // expand into optional type-check + sub-leaves. Type specifier is not required.
      if (!patternNegated && !leafContext && !outerNegated && ctx.Bindings == null
          && innerPat is RecursivePatternSyntax { PropertyPatternClause: not null })
      {
        ctx.Call.Kind = InterceptionKind.Split;
        var innerBindings = new Dictionary<string, CallSiteAnalyzer.LambdaBinding>();
        ctx.Call.SplitRoot = CallSiteAnalyzer.BuildSplitPart(ctx.Syntax, isPattern, ctx.Call, ctx.Compiler, ctx.Ct, innerBindings);
        return ctx.Call.SplitRoot != null;
      }

      // List pattern (arr is [1, _, > 0]): expand into length check + element sub-leaves.
      if (!patternNegated && !leafContext && !outerNegated && ctx.Bindings == null
          && innerPat is ListPatternSyntax)
      {
        ctx.Call.Kind = InterceptionKind.Split;
        var innerBindings = new Dictionary<string, CallSiteAnalyzer.LambdaBinding>();
        ctx.Call.SplitRoot = CallSiteAnalyzer.BuildSplitPart(ctx.Syntax, isPattern, ctx.Call, ctx.Compiler, ctx.Ct, innerBindings);
        return ctx.Call.SplitRoot != null;
      }

      // Simple sub-patterns: relational, constant (including null), and their not-forms.
      switch (innerPat)
      {
        case RelationalPatternSyntax relational when !patternNegated && !outerNegated:
        {
          var (compLabel, opStr) = CallSiteAnalyzer.GetRelationalParts(relational.OperatorToken.Kind());
          ctx.Call.Kind = InterceptionKind.Comparison;
          ctx.Call.ComparisonLabel = compLabel;
          return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, isPattern.Expression, ctx.Bindings, ctx.Display)
                 && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, relational.Expression, CallSiteAnalyzer.StripParens(relational.Expression), ctx.Bindings, ctx.Display);
        }

        case ConstantPatternSyntax nullConst when CallSiteAnalyzer.IsNullOrDefaultLiteral(nullConst.Expression):
        {
          var leftType = ctx.Model.GetTypeInfo(isPattern.Expression, ctx.Ct).Type;
          if (leftType is not { IsReferenceType: true } && !CallSiteAnalyzer.IsNullableValueType(leftType)) return false;
          // is null  → Negated=true (expected null);
          // is not null → Negated=false (expected non-null).
          ctx.Call.Kind = InterceptionKind.Null;
          ctx.Call.Negated = !patternNegated ^ outerNegated;
          return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, isPattern.Expression, ctx.Bindings, ctx.Display);
        }

        case ConstantPatternSyntax otherConst when !CallSiteAnalyzer.IsNullOrDefaultLiteral(CallSiteAnalyzer.StripParens(isPattern.Expression)):
        {
          ctx.Call.Kind = InterceptionKind.Equality;
          ctx.Call.Negated = patternNegated ^ outerNegated;
          return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, isPattern.Expression, ctx.Bindings, ctx.Display)
                 && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, otherConst.Expression, CallSiteAnalyzer.StripParens(otherConst.Expression), ctx.Bindings, ctx.Display);
        }
      }

      // Type-check patterns: obj is Type (Is kind) or obj is object / is not object (Null kind).
      ITypeSymbol? checkedType = innerPat switch
      {
        TypePatternSyntax typePat => ctx.Model.GetTypeInfo(typePat.Type, ctx.Ct).Type,
        DeclarationPatternSyntax declPat when !patternNegated => ctx.Model.GetTypeInfo(declPat.Type, ctx.Ct).Type,
        _ => null,
      };

      if (checkedType == null) return false;

      if (checkedType.SpecialType == SpecialType.System_Object)
      {
        // `x is object` = non-null check (Negated=false);
        // `x is not object` = null check (Negated=true).
        ctx.Call.Kind = InterceptionKind.Null;
        ctx.Call.Negated = patternNegated ^ outerNegated;
        return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, isPattern.Expression, ctx.Bindings, ctx.Display);
      }

      // `obj is not Type` / `obj is not Type u` degrade — Is kind has no Negated flag.
      if (patternNegated) return false;

      ctx.Call.Kind = InterceptionKind.Is;
      ctx.Call.TypeAccessor = ctx.Compiler.TypeAccessor(checkedType);
      return ctx.Call.TypeAccessor != null && CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, isPattern.Expression, ctx.Bindings, ctx.Display);
    }
  }
}
