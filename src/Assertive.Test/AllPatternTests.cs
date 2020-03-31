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

Messages per item:

ids[0] - Expected item to be greater than 10, but item was 1.
ids[1] - Expected item to be greater than 10, but item was 2.
ids[2] - Expected item to be greater than 10, but item was 3.
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
    public void Index_test()
    {
      var numbers = new int[]
      {
        1, 2, 3
      };

      var numbersText = new string[]
      {
        "1", "2", "4"
      };

      ShouldFail(() => numbers.Select((n, i) => new { n, i }).All(x => numbersText[x.i] == x.n.ToString()), @"Expected all items of numbers.Select((n, i) => new { n = n, i = i }) to match the filter numbersText[x.i] == x.n.ToString(), but this item did not: { n = 3, i = 2 }

[2] - Expected numbersText[item.i] to equal item.n.ToString() but numbersText[item.i] was ""4"" while item.n.ToString() was ""3"".");
    }

    [Fact]
    public void Single_item_doesnt_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };
      
      ShouldFail(() => ids.All(i => i >= 2), @"Expected all items of ids to match the filter i >= 2, but this item did not: 1

ids[0] - Expected item to be greater than or equal to 2, but item was 1.");
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

Messages per item:

ids[0] - Expected item to be greater than 1000, but item was 1.
ids[1] - Expected item to be greater than 1000, but item was 2.
ids[2] - Expected item to be greater than 1000, but item was 3.
ids[3] - Expected item to be greater than 1000, but item was 4.
ids[4] - Expected item to be greater than 1000, but item was 5.
ids[5] - Expected item to be greater than 1000, but item was 6.
ids[6] - Expected item to be greater than 1000, but item was 7.
ids[7] - Expected item to be greater than 1000, but item was 8.
ids[8] - Expected item to be greater than 1000, but item was 9.
ids[9] - Expected item to be greater than 1000, but item was 10.
");
    }
  }
}