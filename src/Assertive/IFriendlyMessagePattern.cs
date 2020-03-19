using System;
using System.Linq.Expressions;

namespace Assertive
{
  internal interface IFriendlyMessagePattern
  {
    bool IsMatch(FailedAssertion failedAssertion);
    FormattableString? TryGetFriendlyMessage(FailedAssertion assertion);
    IFriendlyMessagePattern[] SubPatterns { get; }
  }
}
