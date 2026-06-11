using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class IndexOutOfRangeExceptionPatternTests : AssertionTestBase
  {
    private class Item
    {
      public int[] Values { get; set; } = [];
    }

    [Fact]
    public void Out_of_range_is_caught_on_array_with_literal()
    {
      var array = new int[0];

      ShouldFail(() => array[10] == 1);
    }

    [Fact]
    public void Out_of_range_inside_lambda_is_caught()
    {
      var items = new List<Item>
      {
        new Item { Values = new[] { 1, 2, 3 } },
        new Item { Values = new[] { 4 } },
      };

      ShouldFail(() => items.Any(i => i.Values[5] == 99));
    }
    
    [Fact]
    public void Out_of_range_is_caught_on_list_with_literal()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list[4] == 1);
    }
    
    [Fact]
    public void Out_of_range_is_caught_on_list_with_expression()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };

      var myValue = 4;
      
      ShouldFail(() => list[myValue] == 1);
    }
    
    [Fact]
    public void Out_of_range_is_caught_on_array_with_expression()
    {
      var array = new int[2];
      
      var myValue = 4;
      
      ShouldFail(() => array[myValue] == 1);
    }
  }
}