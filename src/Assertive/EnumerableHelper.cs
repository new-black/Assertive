using System.Collections.Generic;
using System.Linq;
using static Assertive.StringQuoter;

namespace Assertive
{
  internal static class EnumerableHelper
  {
    public static string EnumerableToString(IEnumerable<object> collection, bool hasMoreItems = false)
    {
      var count = collection.Count();

      object ItemToString(object o)
      {
        if (o is null) return "null";

        return Quote(o) ?? "null";
      }

      if (count > 10)
      {
        return $"[{string.Join(",", collection.Take(10).Select(ItemToString))},...]";
      }
      
      return $"[{string.Join(",", collection.Select(ItemToString))}{(hasMoreItems ? ",..." : "")}]";
    }
  }
}