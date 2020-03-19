using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class HasValuePattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsHasValueAccess(failedAssertion.ExpressionPossiblyNegated);
    }

    private static bool IsHasValueAccess(Expression expression)
    {
      return expression is MemberExpression memberExpression
             && memberExpression.Member.Name == "HasValue"
             && memberExpression.Expression.Type.IsNullableValueType();
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var memberExpression = (MemberExpression)assertion.ExpressionPossiblyNegated;

      if (assertion.IsNegated)
      {
        return
          $"Expected {memberExpression.Expression} to not have a value but its value was {EvaluateExpression(memberExpression.Expression)}.";
      }

      return $"Expected {memberExpression.Expression} to have a value.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}