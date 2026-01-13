using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class FormatExceptionPatternTests : AssertionTestBase
  {
    [Fact]
    public void Int_Parse_with_invalid_string()
    {
      var input = "abc";

      ShouldFail(() => int.Parse(input) == 123,
        "FormatException caused by calling int.Parse(\"abc\"). \"abc\" is not a valid int.");
    }

    [Fact]
    public void Int_Parse_with_literal_invalid_string()
    {
      ShouldFail(() => int.Parse("not-a-number") == 123,
        "FormatException caused by calling int.Parse(\"not-a-number\"). \"not-a-number\" is not a valid int.");
    }

    [Fact]
    public void Double_Parse_with_invalid_string()
    {
      var input = "invalid";

      ShouldFail(() => double.Parse(input) == 1.5,
        "FormatException caused by calling double.Parse(\"invalid\"). \"invalid\" is not a valid double.");
    }

    [Fact]
    public void DateTime_Parse_with_invalid_string()
    {
      var input = "not-a-date";

      ShouldFail(() => DateTime.Parse(input) > DateTime.MinValue,
        "FormatException caused by calling DateTime.Parse(\"not-a-date\"). \"not-a-date\" is not a valid DateTime.");
    }

    [Fact]
    public void Convert_ToInt32_with_invalid_string()
    {
      var input = "xyz";

      ShouldFail(() => Convert.ToInt32(input) == 0,
        "FormatException caused by calling Convert.ToInt32(\"xyz\"). \"xyz\" is not a valid int.");
    }

    private class Item
    {
      public string Value { get; set; } = "";
    }

    [Fact]
    public void Parse_inside_lambda()
    {
      var items = new List<Item>
      {
        new Item { Value = "123" },
        new Item { Value = "abc" },  // Will fail to parse
      };

      ShouldFail(() => items.All(i => int.Parse(i.Value) > 0),
        """
        FormatException caused by calling int.Parse("abc"). "abc" is not a valid int.

        On item [1] of items:
        { Value = "abc" }
        """);
    }

    [Fact]
    public void Parse_inside_lambda_first_item()
    {
      var items = new List<Item>
      {
        new Item { Value = "invalid" },  // Will fail to parse
        new Item { Value = "456" },
      };

      ShouldFail(() => items.Any(i => int.Parse(i.Value) == 999),
        """
        FormatException caused by calling int.Parse("invalid"). "invalid" is not a valid int.

        On item [0] of items:
        { Value = "invalid" }
        """);
    }
  }
}
