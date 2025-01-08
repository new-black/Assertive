using System;
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

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var left = EqualityPattern.GetLeftSide(assertion.Expression);
      
      var right = left != null ? EqualityPattern.GetRightSide(assertion.Expression, left) : null;

      if (right is { NodeType: ExpressionType.Convert } && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      if (right is { NodeType: ExpressionType.Constant })
      {
        return $"Expected {left} to not equal {right}.";
      }

      return $"Expected {left} to not equal {right} (value: {left?.ToValue()}).";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}