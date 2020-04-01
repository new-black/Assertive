using System.Collections.Generic;
using System.Linq;
using Assertive.Analyzers;
using Assertive.Patterns;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class AnyPatternTests : AssertionTestBase
  {
    [Fact]
    public void Plain_any_works()
    {
      var list = new List<string>();
      
      ShouldFail(() => list.Any(), "Expected list to contain items.");
    }
    
    [Fact]
    public void Any_with_filter_works()
    {
      var list = new List<int>()
      {
        1,2,3
      };
      
      ShouldFail(() => list.Any(l => l > 3), "Expected list to contain items that match the filter l > 3.");
    }
    
    [Fact]
    public void Not_any_works()
    {
      var list = new List<string>()
      {
        "a"
      };
      
      ShouldFail(() => !list.Any(), "Expected list to not contain any items but it actually contained 1 item.");
    }

    [Fact]
    public void AnyPattern_is_triggered()
    {
      var list = new List<string>
      {
        "a", "b", "c"
      };

      var failures = new AssertionFailureAnalyzer(() => list.Any(l => l.Length > 1), null).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && failures[0].FriendlyMessagePattern is AnyPattern);
    }
  }
}