using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
using Xunit;

namespace Assertive.Test
{
  public class AllPatternTests : AssertionTestBase, IDisposable
  {
    private readonly bool originalColors;

    public AllPatternTests()
    {
      originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
    }

    public void Dispose()
    {
     Configuration.Colors.Enabled = originalColors;
    }

    [Fact]
    public void No_items_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };

      ShouldFail(() => ids.All(i => i > 10));
    }

    [Fact]
    public void Not_all_test()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };

      ShouldFail(() => !ids.All(i => i > 0));
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

      ShouldFail(() => numbers.Select((n, i) => new { n, i }).All(x => numbersText[x.i] == x.n.ToString()));
    }

    [Fact]
    public void Single_item_doesnt_match()
    {
      var ids = new List<int>()
      {
        1, 2, 3
      };

      ShouldFail(() => ids.All(i => i >= 2));
    }

    [Fact]
    public void More_than_ten_items_dont_match()
    {
      var ids = Enumerable.Range(1, 100);

      ShouldFail(() => ids.All(i => i > 1000));
    }
  }
}
