using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assertive
{
  internal class FailedAssertion
  {
    internal FailedAssertion(Assertion part, string message, IFriendlyMessagePattern? friendlyMessagePattern)
    {
      Assertion = part;
      Message = message;
      FriendlyMessagePattern = friendlyMessagePattern;
    }

    public Assertion Assertion { get; }
    public string Message { get; }
    public IFriendlyMessagePattern? FriendlyMessagePattern { get; }
  }
}
