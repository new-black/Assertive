using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class NotEqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.Expression.NodeType == ExpressionType.NotEqual
             || EqualityPattern.EqualsMethodShouldBeFalse(failedAssertion.Expression);
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var left = EqualityPattern.GetLeftSide(assertion.Expression);
      
      var right = left != null ? EqualityPattern.GetRightSide(assertion.Expression, left) : null;

      if (right is { NodeType: ExpressionType.Convert } && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }
      
      object? expected = right != null && IsConstantExpression(right) ? right : right?.ToValue();
      object? actual = left?.ToValue();
      
      return new()
      {
        Expected = $"{left}: should not equal {expected}.",
        Actual = $"{left}: {actual}"
      };
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}