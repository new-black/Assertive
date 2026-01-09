using System;
using Assertive.Analyzers;

namespace Assertive.Interfaces
{
  internal interface IFriendlyMessagePattern
  {
    bool IsMatch(FailedAssertion failedAssertion);
    ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion);
    IFriendlyMessagePattern[] SubPatterns { get; }
  }
  
  internal class ExpectedAndActual()
  {
    public required FormattableString Expected { get; init; }
    public required FormattableString? Actual { get; init; }
  }
}
