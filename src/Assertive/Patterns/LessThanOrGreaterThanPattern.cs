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
      return expression.NodeType == ExpressionType.GreaterThan
             || expression.NodeType == ExpressionType.GreaterThanOrEqual
             || expression.NodeType == ExpressionType.LessThan
             || expression.NodeType == ExpressionType.LessThanOrEqual;
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

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var b = (BinaryExpression)assertion.Expression;

      var comparison = GetComparisonLabel(assertion.Expression);
      
      if (b.Right.NodeType == ExpressionType.Constant)
      {
        return
          $"Expected {b.Left} to be {comparison} {b.Right}, but {b.Left} was {b.Left.ToValue()}.";
      }
      else
      {
        return
          $"Expected {b.Left} to be {comparison} {b.Right}, but {b.Left} was {b.Left.ToValue()} while {b.Right} was {b.Right.ToValue()}.";
      }
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}