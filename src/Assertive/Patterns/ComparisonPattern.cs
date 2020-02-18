using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class ComparisonPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return EqualityPattern.IsEqualityComparison(expression)
             || LessThanOrGreaterThanPattern.IsNumericalComparison(expression);
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