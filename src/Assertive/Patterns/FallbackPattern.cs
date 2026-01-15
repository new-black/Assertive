using System.Linq;
using Assertive.Analyzers;
using Assertive.Interfaces;
using Assertive.Plugin;

namespace Assertive.Patterns
{
  internal class FallbackPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return true;
    }

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return null;
    }

    public IFriendlyMessagePattern[] SubPatterns
    {
      get
      {
        // Custom patterns are evaluated first (user-defined, more specific)
        var customPatterns = CustomPatternRegistry.GetPatterns();

        return customPatterns.Count == 0 ? _builtInPatterns : customPatterns.Concat(_builtInPatterns).ToArray();
      }
    }

    private static readonly IFriendlyMessagePattern[] _builtInPatterns =
    [
      new BoolPattern(),
      new ComparisonPattern(),
      new ContainsPattern(),
      new AnyPattern(),
      new AllPattern(),
      new NotAllPattern(),
      new SequenceEqualPattern(),
      new StartsWithAndEndsWithPattern(),
      new ReferenceEqualsPattern(),
      new IsPattern(),
      new NullPattern()
    ];
  }
}