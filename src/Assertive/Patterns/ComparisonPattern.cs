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

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return null;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    [
      new LengthPattern(),
      new EqualityPattern(),
      new LessThanOrGreaterThanPattern()
    ];
  }
}