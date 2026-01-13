using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class KeyNotFoundPatternTests : AssertionTestBase
  {
    [Fact]
    public void Key_not_found_with_literal_key()
    {
      var dict = new Dictionary<string, int>
      {
        ["foo"] = 1,
        ["bar"] = 2
      };

      ShouldFail(() => dict["baz"] == 3,
        """
        KeyNotFoundException caused by accessing key "baz" on dict. Available keys: "foo", "bar".
        """);
    }

    [Fact]
    public void Key_not_found_with_variable_key()
    {
      var dict = new Dictionary<string, int>
      {
        ["foo"] = 1,
        ["bar"] = 2
      };

      var key = "missing";

      ShouldFail(() => dict[key] == 3,
        """
        KeyNotFoundException caused by accessing key key (value: "missing") on dict. Available keys: "foo", "bar".
        """);
    }

    [Fact]
    public void Key_not_found_on_empty_dictionary()
    {
      var dict = new Dictionary<string, int>();

      ShouldFail(() => dict["foo"] == 1,
        """
        KeyNotFoundException caused by accessing key "foo" on dict. Available keys: (empty).
        """);
    }

    [Fact]
    public void Key_not_found_with_int_key()
    {
      var dict = new Dictionary<int, string>
      {
        [1] = "one",
        [2] = "two"
      };

      ShouldFail(() => dict[99] == "ninety-nine",
        """
        KeyNotFoundException caused by accessing key 99 on dict. Available keys: 1, 2.
        """);
    }

    private class Container
    {
      public Dictionary<string, int> Data { get; set; } = new();
    }

    [Fact]
    public void Key_not_found_inside_lambda()
    {
      var containers = new List<Container>
      {
        new Container { Data = new Dictionary<string, int> { ["a"] = 1 } },
        new Container { Data = new Dictionary<string, int> { ["b"] = 2 } },  // Missing key "x"
      };

      ShouldFail(() => containers.Any(c => c.Data["x"] == 99),
        """
        KeyNotFoundException caused by accessing key "x" on c.Data. Available keys: "a".

        On item [0] of containers:
        { Data = { ["a"] = 1 } }
        """);
    }

    [Fact]
    public void Key_not_found_inside_lambda_later_item()
    {
      var containers = new List<Container>
      {
        new Container { Data = new Dictionary<string, int> { ["x"] = 1 } },  // Has key "x"
        new Container { Data = new Dictionary<string, int> { ["y"] = 2 } },  // Missing key "x"
      };

      ShouldFail(() => containers.All(c => c.Data["x"] == 1),
        """
        KeyNotFoundException caused by accessing key "x" on c.Data. Available keys: "y".

        On item [1] of containers:
        { Data = { ["y"] = 2 } }
        """);
    }
  }
}
