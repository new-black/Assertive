using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class EqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return expression.NodeType == ExpressionType.Equal
             || EqualityPattern.EqualsMethodShouldBeTrue(expression);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var right = EqualityPattern.GetRightSide(assertion.Expression);

      if (right.NodeType == ExpressionType.Convert && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }
      
      var left = EqualityPattern.GetLeftSide(assertion.Expression);

      if (right.NodeType == ExpressionType.Constant)
      {
        return $"Expected {left} to equal {right} but {left} was {EvaluateExpression(left)}.";
      }

      return $"Expected {left} to equal {right} but {left} was {EvaluateExpression(left)} while {right} was {EvaluateExpression(right)}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}