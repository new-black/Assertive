using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
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
          Expected = $"{assertion.NegatedExpression}: False",
          Actual = $"True"
        }
        : new ExpectedAndActual()
        {
          Expected = $"{assertion.Expression}: True",
          Actual = $"False"
        };
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    [
      new HasValuePattern()
    ];
  }
}