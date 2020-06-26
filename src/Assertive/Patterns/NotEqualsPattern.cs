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
      
      var right = EqualityPattern.GetRightSide(assertion.Expression, left);

      if (right.NodeType == ExpressionType.Convert && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      if (right.NodeType == ExpressionType.Constant)
      {
        return $"Expected {left} to not equal {right}.";
      }

      return $"Expected {left} to not equal {right} (value: {EvaluateExpression(left)}).";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}