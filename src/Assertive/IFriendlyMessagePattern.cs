using System;
using System.Linq.Expressions;

namespace Assertive
{
  internal interface IFriendlyMessagePattern
  {
    bool IsMatch(Expression expression);
    FormattableString? TryGetFriendlyMessage(FailedAssertion assertion);
    IFriendlyMessagePattern[] SubPatterns { get; }
  }
}
