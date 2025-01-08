using System.Collections.Generic;

namespace Assertive.Helpers
{
  internal class TruncatedList : List<object>
  {
    public int? OriginalCount { get; }

    public TruncatedList(int? originalCount)
    {
      OriginalCount = originalCount;
    }
  }
}