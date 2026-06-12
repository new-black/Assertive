using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class InvalidCastExceptionPatternTests : AssertionTestBase
  {
    [Fact]
    public void Invalid_cast_from_int_to_string()
    {
      object obj = 42;

      ShouldFail(() => (string)obj == "42");
    }

    [Fact]
    public void Invalid_cast_from_string_to_int()
    {
      object obj = "hello";

      ShouldFail(() => (int)obj == 5);
    }

    private class Animal { }
    private class Dog : Animal { }
    private class Cat : Animal { }

    [Fact]
    public void Invalid_cast_between_sibling_types()
    {
      Animal animal = new Dog();

      ShouldFail(() => ((Cat)animal) != null);
    }

    private class Container
    {
      public object Value { get; set; } = "";
    }

    [Fact]
    public void Invalid_cast_inside_lambda()
    {
      var containers = new List<Container>
      {
        new Container { Value = "text" },
        new Container { Value = 123 },  // Will fail to cast to string
      };

      ShouldFail(() => containers.All(c => (string)c.Value != null));
    }

    [Fact]
    public void Invalid_cast_inside_lambda_first_item()
    {
      var containers = new List<Container>
      {
        new Container { Value = 42 },  // Will fail to cast to string
        new Container { Value = "text" },
      };

      ShouldFail(() => containers.Any(c => (string)c.Value == "42"));
    }
  }
}
