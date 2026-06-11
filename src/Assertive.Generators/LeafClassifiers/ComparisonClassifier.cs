using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class ComparisonClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      if (expr is not BinaryExpressionSyntax binary
          || binary.Kind() is not (SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression))
        return null;

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

      var left = CallSiteAnalyzer.StripParens(binary.Left);
      var right = CallSiteAnalyzer.StripParens(binary.Right);

      if (CallSiteAnalyzer.IsNullOrDefaultLiteral(left) || CallSiteAnalyzer.IsNullOrDefaultLiteral(right))
      {
        return false;
      }

      ctx.Call.ComparisonLabel = comparisonLabel;

      if (CallSiteAnalyzer.TryGetLengthAccess(left, out var countLabel, out var operand, out var filterSource))
      {
        ctx.Call.Kind = InterceptionKind.Length;
        ctx.Call.CountLabel = countLabel;
        ctx.Call.OperandDisplay = ctx.Display?.Invoke(operand) ?? operand.ToString();
        ctx.Call.FilterSource = filterSource;
      }
      else
      {
        ctx.Call.Kind = InterceptionKind.Comparison;
      }

      return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, binary.Left, ctx.Bindings, ctx.Display)
             && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, binary.Right, right, ctx.Bindings, ctx.Display);
    }
  }
}
