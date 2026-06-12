using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class ArgumentNullPatternTests : AssertionTestBase
  {
    private class Container
    {
      public List<string>? Items { get; set; }
    }

    [Fact]
    public void Cause_of_ArgumentNull_is_found_single_level()
    {
      List<string>? items = null;

      ShouldFail(() => items.Where(i => i.StartsWith("abc")).Any());
    }

    [Fact]
    public void Cause_of_ArgumentNull_is_found_two_levels()
    {
      List<string>? items = null;

      ShouldFail(() => items.Where(i => i.StartsWith("abc")).Count(x => x.EndsWith("123")) == 0);
    }

    [Fact]
    public void Cause_of_ArgumentNull_inside_lambda_is_found()
    {
      var containers = new List<Container>
      {
        new Container { Items = new List<string> { "abc" } },
        new Container { Items = null },  // This one will throw ArgumentNullException
      };

      ShouldFail(() => containers.Any(c => c.Items.Where(i => i.StartsWith("x")).Any()));
    }
  }
}