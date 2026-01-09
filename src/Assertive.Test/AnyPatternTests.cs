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
      
      ShouldFail(() => list.Any(), "Collection list should contain some items.", "It contained no items.");
    }
    
    [Fact]
    public void Any_with_filter_works()
    {
      var list = new List<int>()
      {
        1,2,3
      };
      
      ShouldFail(() => list.Any(l => l > 3), "Collection list should contain some items that match the filter l > 3.", "It contained no items matching the filter.");
    }
    
    [Fact]
    public void Not_any_works()
    {
      var list = new List<string>()
      {
        "a"
      };
      
      ShouldFail(() => !list.Any(), "Collection list should not contain any items.", "It contained 1 item");
    }

    [Fact]
    public void AnyPattern_is_triggered()
    {
      var list = new List<string>
      {
        "a", "b", "c"
      };

      var failures = new AssertionFailureAnalyzer(new AssertionFailureContext(new Assertion(() => list.Any(l => l.Length > 1), null, null), null)).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && failures[0].FriendlyMessagePattern is AnyPattern);
    }
  }
}