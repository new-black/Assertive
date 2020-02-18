using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class NotEqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return expression.NodeType == ExpressionType.NotEqual
             || EqualityPattern.EqualsMethodShouldBeFalse(expression);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
     
      var left = EqualityPattern.GetLeftSide(assertion.Expression);
      
      var right = EqualityPattern.GetRightSide(assertion.Expression, left);

      if (right.NodeType == ExpressionType.Convert && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      if (right.NodeType == ExpressionType.Constant)
      {
        return $"Expected {left} to not equal {right}.";
      }

      return $"Expected {left} to not equal {right} but they were equal (value: {EvaluateExpression(left)}).";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}