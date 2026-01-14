using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class ArgumentOutOfRangeExceptionPatternTests : AssertionTestBase
  {
    [Fact]
    public void Substring_with_index_too_large()
    {
      var str = "hello";

      ShouldFail(() => str.Substring(10) == "world",
        "ArgumentOutOfRangeException caused by calling Substring(10) on str (length: 5).");
    }

    [Fact]
    public void Substring_with_variable_index()
    {
      var str = "hello";
      var startIndex = 10;

      ShouldFail(() => str.Substring(startIndex) == "world",
        "ArgumentOutOfRangeException caused by calling Substring(startIndex (value: 10)) on str (length: 5).");
    }

    [Fact]
    public void Substring_with_negative_index()
    {
      var str = "hello";

      ShouldFail(() => str.Substring(-1) == "h",
        "ArgumentOutOfRangeException caused by calling Substring(-1) on str (length: 5).");
    }

    [Fact]
    public void Substring_with_length_too_large()
    {
      var str = "hello";

      ShouldFail(() => str.Substring(3, 10) == "lo",
        "ArgumentOutOfRangeException caused by calling Substring(3, 10) on str (length: 5).");
    }

    private class Item
    {
      public string Text { get; set; } = "";
      public int StartIndex { get; set; }
    }

    [Fact]
    public void Substring_inside_lambda()
    {
      var items = new List<Item>
      {
        new Item { Text = "hello", StartIndex = 2 },
        new Item { Text = "hi", StartIndex = 10 },  // Will throw
      };

      ShouldFail(() => items.All(i => i.Text.Substring(i.StartIndex).Length > 0),
        """
        ArgumentOutOfRangeException caused by calling Substring(i.StartIndex (value: 10)) on i.Text (length: 2).

        On item [1] of items:
        { Text = "hi", StartIndex = 10 }
        """);
    }

    [Fact]
    public void Substring_inside_lambda_first_item()
    {
      var items = new List<Item>
      {
        new Item { Text = "x", StartIndex = 5 },  // Will throw
        new Item { Text = "hello", StartIndex = 2 },
      };

      ShouldFail(() => items.Any(i => i.Text.Substring(i.StartIndex) == "test"),
        """
        ArgumentOutOfRangeException caused by calling Substring(i.StartIndex (value: 5)) on i.Text (length: 1).

        On item [0] of items:
        { Text = "x", StartIndex = 5 }
        """);
    }
  }
}
