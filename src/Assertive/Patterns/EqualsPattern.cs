using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class EqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.Expression.NodeType == ExpressionType.Equal
             || EqualityPattern.EqualsMethodShouldBeTrue(failedAssertion.Expression);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var left = EqualityPattern.GetLeftSide(assertion.Expression);
      
      var right = left != null ? EqualityPattern.GetRightSide(assertion.Expression, left) : null;

      if (right != null && right.NodeType == ExpressionType.Convert && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      if (right != null && IsConstantExpression(right))
      {
        return $"Expected {left} to equal {right} but {left} was {left?.ToValue()}.";
      }

      return $"Expected {left} to equal {right} but {left} was {left?.ToValue()} while {right} was {right?.ToValue()}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}