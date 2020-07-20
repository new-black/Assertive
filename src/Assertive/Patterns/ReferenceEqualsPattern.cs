using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class ReferenceEqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.ExpressionWithoutNegation is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == nameof(ReferenceEquals)
             && methodCallExpression.Arguments.Count == 2;
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCall = (MethodCallExpression)assertion.ExpressionWithoutNegation;

      var arg1 = methodCall.Arguments[0];
      var arg2 = methodCall.Arguments[1];

      var result = assertion.IsNegated ? (FormattableString)$"Expected {arg1} and {arg2} to be a different instances." : $"Expected {arg1} and {arg2} to be the same instance.";

      return result;
    }

    public IFriendlyMessagePattern[] SubPatterns => Array.Empty<IFriendlyMessagePattern>();
  }
}