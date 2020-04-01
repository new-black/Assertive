using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class ComparisonPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return EqualityPattern.IsEqualityComparison(failedAssertion.Expression)
             || LessThanOrGreaterThanPattern.IsNumericalComparison(failedAssertion.Expression);
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return default;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    {
      new LengthPattern(),
      new EqualityPattern(),
      new LessThanOrGreaterThanPattern()
    };
  }
}