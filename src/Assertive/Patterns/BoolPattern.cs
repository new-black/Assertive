using System;
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

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return assertion.IsNegated ? 
        $"Expected {assertion.NegatedExpression} to be false." 
        : (FormattableString)$"Expected {assertion.Expression} to be true.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    [
      new HasValuePattern()
    ];
  }
}