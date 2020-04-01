using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class FallbackPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return true;
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return null;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    {
      new BoolPattern(),
      new ComparisonPattern(),
      new ContainsPattern(),
      new AnyPattern(),
      new AllPattern(),
      new NotAllPattern(),
      new SequenceEqualPattern(),
      new StartsWithAndEndsWithPattern()
    };
  }
}