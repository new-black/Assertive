using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
using Xunit;

namespace Assertive.Test
{
  public class AllPatternTests : AssertionTestBase, IDisposable
  {
    public AllPatternTests()
    {
      Configuration.Colors.Enabled = false;
    }

    public void Dispose()
    {
     Configuration.Colors.Enabled = true;
    }

    [Fact]
    public void No_items_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };

      ShouldFail(() => ids.All(i => i > 10), "All items of ids should match the filter i > 10", @"These 3 items did not:

[0]: 1,
[1]: 2,
[2]: 3

Messages per item:

ids[0]
[EXPECTED]
item should be greater than 10.
[ACTUAL]
item: 1.

ids[1]
[EXPECTED]
item should be greater than 10.
[ACTUAL]
item: 2.

ids[2]
[EXPECTED]
item should be greater than 10.
[ACTUAL]
item: 3.
");
    }

    [Fact]
    public void Not_all_test()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };

      ShouldFail(() => !ids.All(i => i > 0), "Not all items of ids should match the filter i > 0.", "");
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

      ShouldFail(() => numbers.Select((n, i) => new { n, i }).All(x => numbersText[x.i] == x.n.ToString()),
        "All items of numbers.Select((n, i) => new { n = n, i = i }) should match the filter numbersText[x.i] == x.n.ToString()",
        @"This item did not: 
{ n = 3, i = 2 }

[2]
[EXPECTED]
numbersText[item.i]: ""3""
[ACTUAL]
numbersText[item.i]: ""4""

Strings differ at index 0:

Expected:""[4]""
Actual:""[3]""");
    }

    [Fact]
    public void Single_item_doesnt_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };

      ShouldFail(() => ids.All(i => i >= 2), "All items of ids should match the filter i >= 2", @"This item did not: 
1

ids[0]
[EXPECTED]
item should be greater than or equal to 2.
[ACTUAL]
item: 1.
");
    }

    [Fact]
    public void More_than_ten_items_dont_match()
    {
      var ids = Enumerable.Range(1, 100);

      ShouldFail(() => ids.All(i => i > 1000), "All items of ids should match the filter i > 1000", @"These 100 items did not (first 10):

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

ids[0]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 1.

ids[1]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 2.

ids[2]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 3.

ids[3]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 4.

ids[4]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 5.

ids[5]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 6.

ids[6]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 7.

ids[7]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 8.

ids[8]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 9.

ids[9]
[EXPECTED]
item should be greater than 1000.
[ACTUAL]
item: 10.
");
    }
  }
}