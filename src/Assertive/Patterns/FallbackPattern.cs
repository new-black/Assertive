using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class FallbackPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return true;
    }

    public FormattableString TryGetFriendlyMessage(Assertion assertion)
    {
      return $"Assertion failed: {assertion.Expression}";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    {
      new ComparisonPattern(),
      new ContainsPattern()
    };
  }
}