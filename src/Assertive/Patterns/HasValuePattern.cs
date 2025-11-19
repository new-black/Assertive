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
      return IsHasValueAccess(failedAssertion.ExpressionWithoutNegation);
    }

    private static bool IsHasValueAccess(Expression expression)
    {
      return expression is MemberExpression { Member.Name: "HasValue" } memberExpression 
             && memberExpression.Expression?.Type.IsNullableValueType() == true;
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var memberExpression = (MemberExpression)assertion.ExpressionWithoutNegation;

      if (assertion.IsNegated)
      {
        return new ExpectedAndActual()
        {
          Expected = $"{memberExpression.Expression} should not have a value.",
          Actual = $"Value: {memberExpression.Expression?.ToValue()}."
        };
      }
      
      return new ExpectedAndActual()
      {
        Expected = $"{memberExpression.Expression} should have a value.",
        Actual = $"It was null."
      };
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}