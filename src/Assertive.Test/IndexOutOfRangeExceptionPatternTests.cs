using System.Collections.Generic;
using Xunit;

namespace Assertive.Test
{
  public class IndexOutOfRangeExceptionPatternTests : AssertionTestBase
  {
    [Fact]
    public void Out_of_range_is_caught_on_array_with_literal()
    {
      var array = new int[0];

      ShouldFail(() => array[10] == 1, "IndexOutOfRangeException caused by accessing index 10 on array, actual length was 0.");
    }
    
    [Fact]
    public void Out_of_range_is_caught_on_list_with_literal()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => list[4] == 1, "ArgumentOutOfRangeException caused by accessing index 4 on list, actual count was 3.");
    }
    
    [Fact]
    public void Out_of_range_is_caught_on_list_with_expression()
    {
      var list = new List<int>()
      {
        1, 2, 3
      };

      var myValue = 4;
      
      ShouldFail(() => list[myValue] == 1, "ArgumentOutOfRangeException caused by accessing index myValue (value: 4) on list, actual count was 3.");
    }
    
    [Fact]
    public void Out_of_range_is_caught_on_array_with_expression()
    {
      var array = new int[2];
      
      var myValue = 4;
      
      ShouldFail(() => array[myValue] == 1, "IndexOutOfRangeException caused by accessing index myValue (value: 4) on array, actual length was 2.");
    }
  }
}