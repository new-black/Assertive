using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class AnyPatternTests : AssertionTestBase
  {
    [Fact]
    public void Plain_any_works()
    {
      var list = new List<string>();
      
      ShouldFail(() => list.Any());
    }
    
    [Fact]
    public void Any_with_filter_works()
    {
      var list = new List<int>()
      {
        1,2,3
      };
      
      ShouldFail(() => list.Any(l => l > 3));
    }
    
    [Fact]
    public void Not_any_works()
    {
      var list = new List<string>()
      {
        "a"
      };
      
      ShouldFail(() => !list.Any());
    }

    private class Foo
    {
      public string Name { get; set; }
    }
    
    [Fact]
    public void Any_with_filter_works_on_private_class()
    {
      var list = new List<Foo>()
      {
        new Foo() { Name = "a" },
        new Foo() { Name = "b" },
        new Foo() { Name = "c" }
      };
      
      ShouldFail(() => list.Any(l => l.Name == "d"));
    }
  }
}