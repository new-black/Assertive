using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class DivideByZeroExceptionPatternTests : AssertionTestBase
  {
    [Fact]
    public void Divide_by_zero_with_literal()
    {
      var a = 10;

      ShouldFail(() => a / 0 == 0,
        "DivideByZeroException caused by dividing a by 0.");
    }

    [Fact]
    public void Divide_by_zero_with_variable()
    {
      var a = 10;
      var b = 0;

      ShouldFail(() => a / b == 0,
        "DivideByZeroException caused by dividing a by b (value: 0).");
    }

    [Fact]
    public void Modulo_by_zero_with_literal()
    {
      var a = 10;

      ShouldFail(() => a % 0 == 0,
        "DivideByZeroException caused by modulo a by 0.");
    }

    [Fact]
    public void Modulo_by_zero_with_variable()
    {
      var a = 10;
      var b = 0;

      ShouldFail(() => a % b == 0,
        "DivideByZeroException caused by modulo a by b (value: 0).");
    }

    private class Item
    {
      public int Numerator { get; set; }
      public int Denominator { get; set; }
    }

    [Fact]
    public void Divide_by_zero_inside_lambda()
    {
      var items = new List<Item>
      {
        new Item { Numerator = 10, Denominator = 2 },
        new Item { Numerator = 20, Denominator = 0 },  // Will throw
      };

      ShouldFail(() => items.All(i => i.Numerator / i.Denominator > 0),
        """
        DivideByZeroException caused by dividing i.Numerator by i.Denominator (value: 0).

        On item [1] of items:
        { Numerator = 20, Denominator = 0 }
        """);
    }

    [Fact]
    public void Divide_by_zero_inside_lambda_first_item()
    {
      var items = new List<Item>
      {
        new Item { Numerator = 10, Denominator = 0 },  // Will throw
        new Item { Numerator = 20, Denominator = 5 },
      };

      ShouldFail(() => items.Any(i => i.Numerator / i.Denominator == 999),
        """
        DivideByZeroException caused by dividing i.Numerator by i.Denominator (value: 0).

        On item [0] of items:
        { Numerator = 10, Denominator = 0 }
        """);
    }
  }
}
