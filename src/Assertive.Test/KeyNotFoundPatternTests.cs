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

      ShouldFail(() => dict["baz"] == 3);
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

      ShouldFail(() => dict[key] == 3);
    }

    [Fact]
    public void Key_not_found_on_empty_dictionary()
    {
      var dict = new Dictionary<string, int>();

      ShouldFail(() => dict["foo"] == 1);
    }

    [Fact]
    public void Key_not_found_with_int_key()
    {
      var dict = new Dictionary<int, string>
      {
        [1] = "one",
        [2] = "two"
      };

      ShouldFail(() => dict[99] == "ninety-nine");
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

      ShouldFail(() => containers.Any(c => c.Data["x"] == 99));
    }

    [Fact]
    public void Key_not_found_inside_lambda_later_item()
    {
      var containers = new List<Container>
      {
        new Container { Data = new Dictionary<string, int> { ["x"] = 1 } },  // Has key "x"
        new Container { Data = new Dictionary<string, int> { ["y"] = 2 } },  // Missing key "x"
      };

      ShouldFail(() => containers.All(c => c.Data["x"] == 1));
    }
  }
}
