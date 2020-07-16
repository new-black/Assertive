using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Helpers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

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
          $"Expected {memberExpression.Expression} to not have a value but its value was {memberExpression.Expression.ToValue()}.";
      }

      return $"Expected {memberExpression.Expression} to have a value.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}