using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class LessThanOrGreaterThanPattern : IFriendlyMessagePattern
  {
    public static bool IsNumericalComparison(Expression expression)
    {
      return expression.NodeType is ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;
    }
    
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsNumericalComparison(failedAssertion.Expression);
    }

    public static string GetComparisonLabel(Expression expression)
    {
      return expression.NodeType switch
      {
        ExpressionType.GreaterThanOrEqual => "greater than or equal to",
        ExpressionType.GreaterThan => "greater than",
        ExpressionType.LessThan => "less than",
        ExpressionType.LessThanOrEqual => "less than or equal to",
        _ => throw new InvalidOperationException("Unhandled comparison")
      };
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var b = (BinaryExpression)assertion.Expression;

      var comparison = GetComparisonLabel(assertion.Expression);
      
      if (b.Right.NodeType == ExpressionType.Constant)
      {
        return new ExpectedAndActual()
        {
          Expected = $"{b.Left} should be {comparison} {b.Right}.",
          Actual = $"{b.Left}: {b.Left.ToValue()}."
        };
      }
      else
      {
        return new ExpectedAndActual()
        {
          Expected = $"{b.Left} should be {comparison} {b.Right}.",
          Actual = $"""
                    {b.Left}: {b.Left.ToValue()}
                    {b.Right}: {b.Right.ToValue()}
                    """
        };
      }
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}