using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

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
      
      var right = EqualityPattern.GetRightSide(assertion.Expression, left);

      if (right.NodeType == ExpressionType.Convert && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      if (IsConstantExpression(right))
      {
        return $"Expected {left} to equal {right} but {left} was {EvaluateExpression(left)}.";
      }

      return $"Expected {left} to equal {right} but {left} was {EvaluateExpression(left)} while {right} was {EvaluateExpression(right)}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}