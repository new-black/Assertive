using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class LinqElementCountPatternTests : AssertionTestBase
  {
    [Fact]
    public void Single_on_empty_collection_is_caught()
    {
      var list = new List<int>();
      
      ShouldFail(() => list.Single() == 10);
    }
    
    [Fact]
    public void Single_on_empty_collection_is_caught_when_a_filter_is_used()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list.Single(l => l > 3) == 10);
    }
    
    [Fact]
    public void Single_on_collection_that_contains_multiple_items_that_match_filter_is_caught()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list.Single(l => l > 1) == 10);
    }
    
    [Fact]
    public void Single_on_large_enumerable_is_caught()
    {
      var list = Enumerable.Range(0, 1000);
      
      ShouldFail(() => list.Single() == 10);
    }
    
    [Fact]
    public void Single_on_large_list_is_caught()
    {
      var list = Enumerable.Range(0, 1000).ToList();
      
      ShouldFail(() => list.Single() == 10);
    }
    
    [Fact]
    public void First_on_empty_collection_is_caught()
    {
      var list = new List<int>();
      
      ShouldFail(() => list.First() == 10);
    }
    
    [Fact]
    public void Single_on_empty_collection_is_caught_when_there_is_also_a_non_empty_collection()
    {
      var list = new List<int>();
      
      var list2 = new List<string>()
      {
        "abc"
      };
      
      ShouldFail(() => list2.Single() != null && list.Single() != 0);
    }
    
    [Fact]
    public void First_on_empty_collection_is_caught_when_there_is_also_a_non_empty_collection()
    {
      var list = new List<int>();
      
      var list2 = new List<string>()
      {
        "abc"
      };
      
      ShouldFail(() => list2.First() != null && list.First() != 0);
    }
    
    [Fact]
    public void Single_on_collection_containing_multiple_items_is_caught()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list.Single() == 10);
    }

    class Something
    {
      public List<string> Items { get; set; }
    }

    [Fact]
    public void List_of_list_works()
    {
      var list = new List<Something>()
      {
        new Something(),
        new Something()
      };

      ShouldFail(() => list.Single().Items.Single() != null);
    }

    [Fact]
    public void Single_inside_lambda_on_empty_collection_is_caught()
    {
      var containers = new List<Something>
      {
        new Something { Items = new List<string> { "only-one" } },
        new Something { Items = new List<string>() },  // Empty - will throw
      };

      ShouldFail(() => containers.Any(c => c.Items.Single() == "x"));
    }

    [Fact]
    public void Single_inside_lambda_on_multiple_items_is_caught()
    {
      var containers = new List<Something>
      {
        new Something { Items = new List<string> { "only-one" } },
        new Something { Items = new List<string> { "a", "b", "c" } },  // Multiple - will throw
      };

      ShouldFail(() => containers.Any(c => c.Items.Single() == "x"));
    }
  }
}
