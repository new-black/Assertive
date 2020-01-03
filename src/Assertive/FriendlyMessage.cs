using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assertive
{
  internal class FriendlyMessage
  {
    internal FriendlyMessage(string message, IFriendlyMessagePattern pattern)
    {
      Message = message;
      Pattern = pattern;
    }

    public string Message { get; set; }
    public IFriendlyMessagePattern Pattern { get; set; }
  }
}
