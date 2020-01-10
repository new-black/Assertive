using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class AllPatternTests : AssertionTestBase
  {
    [Fact]
    public void No_items_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => ids.All(i => i > 10), @"Expected all items of ids to match the filter i > 10, but these 3 items did not:

[0]: 1,
[1]: 2,
[2]: 3
");
    }
    
    [Fact]
    public void Not_all_test()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => !ids.All(i => i > 0), "Did not expect all items of ids to match the filter i > 0.");
    }
    
    [Fact]
    public void Single_item_doesnt_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => ids.All(i => i >= 2), @"Expected all items of ids to match the filter i >= 2, but this item did not: 1");
    }
    
    [Fact]
    public void More_than_ten_items_dont_match()
    {
      var ids = Enumerable.Range(1, 100);
      
      ShouldFail(() => ids.All(i => i > 1000), @"Expected all items of ids to match the filter i > 1000, but these 100 items did not (first 10):

[0]: 1,
[1]: 2,
[2]: 3,
[3]: 4,
[4]: 5,
[5]: 6,
[6]: 7,
[7]: 8,
[8]: 9,
[9]: 10
");
    }
  }
}