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

      ShouldFail(() => int.Parse(input) == 123);
    }

    [Fact]
    public void Int_Parse_with_literal_invalid_string()
    {
      ShouldFail(() => int.Parse("not-a-number") == 123);
    }

    [Fact]
    public void Double_Parse_with_invalid_string()
    {
      var input = "invalid";

      ShouldFail(() => double.Parse(input) == 1.5);
    }

    [Fact]
    public void DateTime_Parse_with_invalid_string()
    {
      var input = "not-a-date";

      ShouldFail(() => DateTime.Parse(input) > DateTime.MinValue);
    }

    [Fact]
    public void Convert_ToInt32_with_invalid_string()
    {
      var input = "xyz";

      ShouldFail(() => Convert.ToInt32(input) == 0);
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

      ShouldFail(() => items.All(i => int.Parse(i.Value) > 0));
    }

    [Fact]
    public void Parse_inside_lambda_first_item()
    {
      var items = new List<Item>
      {
        new Item { Value = "invalid" },  // Will fail to parse
        new Item { Value = "456" },
      };

      ShouldFail(() => items.Any(i => int.Parse(i.Value) == 999));
    }
  }
}
