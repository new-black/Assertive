using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class EqualityClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      // Binary == / !=
      if (expr is BinaryExpressionSyntax binary
          && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
      {
        return ClassifyBinaryEquality(ctx, binary, outerNegated);
      }

      // Instance .Equals(x)
      if (expr is InvocationExpressionSyntax
          {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" } equalsAccess,
            ArgumentList.Arguments.Count: 1
          } equalsCall
          && ctx.Model.GetSymbolInfo(equalsCall, ctx.Ct).Symbol is IMethodSymbol
          {
            Name: "Equals", IsStatic: false, ReturnType.SpecialType: SpecialType.System_Boolean, Parameters.Length: 1
          })
      {
        var right = CallSiteAnalyzer.StripParens(equalsCall.ArgumentList.Arguments[0].Expression);

        if (CallSiteAnalyzer.IsNullOrDefaultLiteral(right) || CallSiteAnalyzer.IsNullOrDefaultLiteral(CallSiteAnalyzer.StripParens(equalsAccess.Expression)))
        {
          return false;
        }

        ctx.Call.Kind = InterceptionKind.Equality;
        return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, equalsAccess.Expression, ctx.Bindings, ctx.Display)
               && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, equalsCall.ArgumentList.Arguments[0].Expression, right, ctx.Bindings, ctx.Display);
      }

      // ReferenceEquals(x, y)
      if (expr is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 2 } refEqCall
          && ctx.Model.GetSymbolInfo(refEqCall, ctx.Ct).Symbol is IMethodSymbol
          {
            Name: "ReferenceEquals", IsStatic: true, Parameters.Length: 2, ContainingType.SpecialType: SpecialType.System_Object
          }
          && System.Linq.Enumerable.All(refEqCall.ArgumentList.Arguments, a => a.NameColon == null))
      {
        ctx.Call.Kind = InterceptionKind.ReferenceEquals;
        return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, refEqCall.ArgumentList.Arguments[0].Expression, ctx.Bindings, ctx.Display)
               && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, refEqCall.ArgumentList.Arguments[1].Expression,
                 CallSiteAnalyzer.StripParens(refEqCall.ArgumentList.Arguments[1].Expression), ctx.Bindings, ctx.Display);
      }

      return null;
    }

    private static bool? ClassifyBinaryEquality(ClassificationContext ctx, BinaryExpressionSyntax binary, bool outerNegated)
    {
      var isNotEquals = binary.IsKind(SyntaxKind.NotEqualsExpression);
      var left = CallSiteAnalyzer.StripParens(binary.Left);
      var right = CallSiteAnalyzer.StripParens(binary.Right);

      if (CallSiteAnalyzer.IsNullOrDefaultLiteral(left))
      {
        return false;
      }

      if (CallSiteAnalyzer.IsNullOrDefaultLiteral(right))
      {
        // Null-check routing (NullPattern): `x == null`, `x != default`, ... The old
        // pattern only matched reference types (plus nullables for the null literal);
        // `someStruct == default` stays degraded.
        var leftType = ctx.Model.GetTypeInfo(left, ctx.Ct).Type;

        var nullCheckable = right.IsKind(SyntaxKind.NullLiteralExpression)
          ? leftType is { IsReferenceType: true } || CallSiteAnalyzer.IsNullableValueType(leftType)
          : leftType is { IsReferenceType: true };

        if (!nullCheckable)
        {
          return false;
        }

        ctx.Call.Kind = InterceptionKind.Null;
        ctx.Call.Negated = !isNotEquals ^ outerNegated; // true = expected null
        return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, binary.Left, ctx.Bindings, ctx.Display);
      }

      if (CallSiteAnalyzer.TryGetLengthAccess(left, out var countLabel, out var operand, out var filterSource))
      {
        // The old LengthPattern did not match negated forms.
        if (outerNegated)
        {
          return false;
        }

        ctx.Call.Kind = InterceptionKind.Length;
        ctx.Call.CountLabel = countLabel;
        ctx.Call.OperandDisplay = ctx.Display?.Invoke(operand) ?? operand.ToString();
        ctx.Call.FilterSource = filterSource;
        ctx.Call.ComparisonLabel = isNotEquals ? "not equal to" : "equal to";
        return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, binary.Left, ctx.Bindings, ctx.Display)
               && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, binary.Right, right, ctx.Bindings, ctx.Display);
      }

      if (CallSiteAnalyzer.IsLengthOrCountAccess(left) || CallSiteAnalyzer.IsLengthOrCountAccess(right))
      {
        // LongCount and friends: routed to other patterns historically; stay degraded.
        return false;
      }

      ctx.Call.Kind = InterceptionKind.Equality;
      ctx.Call.Negated = isNotEquals ^ outerNegated;
      return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, binary.Left, ctx.Bindings, ctx.Display)
             && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, binary.Right, right, ctx.Bindings, ctx.Display);
    }
  }
}
