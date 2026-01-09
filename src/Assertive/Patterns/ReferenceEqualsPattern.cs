using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class ReferenceEqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.ExpressionWithoutNegation is MethodCallExpression { Method.Name: nameof(ReferenceEquals), Arguments.Count: 2 };
    }

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCall = (MethodCallExpression)assertion.ExpressionWithoutNegation;

      var arg1 = methodCall.Arguments[0];
      var arg2 = methodCall.Arguments[1];

      if (assertion.IsNegated)
      {
        return new ExpectedAndActual()
        {
          Expected = $"{arg1} and {arg2} should be different instances.",
          Actual = null
        };
      }
      else
      {
        return new ExpectedAndActual()
        {
          Expected = $"{arg1} and {arg2} should be the same instance.",
          Actual = $"""
                    {arg1}: {arg1.ToValue()}
                    {arg2}: {arg2.ToValue()}
                    """
        };
      }
    }

    public IFriendlyMessagePattern[] SubPatterns => [];
  }
}