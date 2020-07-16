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
      
      ShouldFail(() => list.Single() == 10, 
        @"InvalidOperationException caused by calling Single on list which contains no elements.");
    }
    
    [Fact]
    public void Single_on_empty_collection_is_caught_when_a_filter_is_used()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list.Single(l => l > 3) == 10, 
        @"InvalidOperationException caused by calling Single(l => l > 3) on list which contains no elements that match the filter.

Value of list: [ 1, 2, 3 ]");
    }
    
    [Fact]
    public void Single_on_collection_that_contains_multiple_items_that_match_filter_is_caught()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list.Single(l => l > 1) == 10, 
        @"InvalidOperationException caused by calling Single(l => l > 1) on list which contains more than one element that matches the filter. Actual element count: 2.

Value of list.Single(l => l > 1): [ 2, 3 ]
");
    }
    
    [Fact]
    public void Single_on_large_collection_is_caught()
    {
      var list = Enumerable.Range(0, 1000);
      
      ShouldFail(() => list.Single() == 10, 
        @"InvalidOperationException caused by calling Single on list which contains more than one element. Actual element count: 1000.

Value of list: [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ... ]
");
    }
    
    [Fact]
    public void First_on_empty_collection_is_caught()
    {
      var list = new List<int>();
      
      ShouldFail(() => list.First() == 10, 
        "InvalidOperationException caused by calling First on list which contains no elements.");
    }
    
    [Fact]
    public void Single_on_empty_collection_is_caught_when_there_is_also_a_non_empty_collection()
    {
      var list = new List<int>();
      
      var list2 = new List<string>()
      {
        "abc"
      };
      
      ShouldFail(() => list2.Single() != null && list.Single() != 0, 
        "InvalidOperationException caused by calling Single on list which contains no elements.");
    }
    
    [Fact]
    public void First_on_empty_collection_is_caught_when_there_is_also_a_non_empty_collection()
    {
      var list = new List<int>();
      
      var list2 = new List<string>()
      {
        "abc"
      };
      
      ShouldFail(() => list2.First() != null && list.First() != 0, 
        "InvalidOperationException caused by calling First on list which contains no elements.");
    }
    
    [Fact]
    public void Single_on_collection_containing_multiple_items_is_caught()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list.Single() == 10, 
        @"InvalidOperationException caused by calling Single on list which contains more than one element. Actual element count: 3.

Value of list: [ 1, 2, 3 ]");
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
      
      ShouldFail(() => list.Single().Items.Single() != null, 
        @"InvalidOperationException caused by calling Single on list which contains more than one element. Actual element count: 2.

Value of list: [ { }, { } ]");
    }
  }
}