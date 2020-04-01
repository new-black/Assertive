using System;
using Assertive.Analyzers;

namespace Assertive.Interfaces
{
  internal interface IFriendlyMessagePattern
  {
    bool IsMatch(FailedAssertion failedAssertion);
    FormattableString? TryGetFriendlyMessage(FailedAssertion assertion);
    IFriendlyMessagePattern[] SubPatterns { get; }
  }
}
