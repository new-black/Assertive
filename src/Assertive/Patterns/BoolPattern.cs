using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class BoolPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.ExpressionWithoutNegation is MemberExpression;
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return assertion.IsNegated
        ? new ExpectedAndActual()
        {
          Expected = $"{assertion.NegatedExpression}: {Expression.Constant(false)}",
          Actual = $"{Expression.Constant(true)}"
        }
        : new ExpectedAndActual()
        {
          Expected = $"{assertion.Expression}: {Expression.Constant(true)}",
          Actual = $"{Expression.Constant(false)}"
        };
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    [
      new HasValuePattern()
    ];
  }
}