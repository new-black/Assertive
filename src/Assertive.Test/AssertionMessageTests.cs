using System;
using System.Globalization;
using Assertive.Config;
using Xunit;

namespace Assertive.Test
{
  public class AssertionMessageTests : AssertionTestBase, IDisposable
  {
    private readonly bool originalColors;

    public AssertionMessageTests()
    {
      originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
    }

    public void Dispose()
    {
      Configuration.Colors.Enabled = originalColors;
    }

    [Fact]
    public void Message_is_part_of_output()
    {
      var order = new
      {
        Amount = 10
      };
      
      ShouldFailWith(() => Assert.That(() => order.Amount > 20, "Expected orderID to be more than 20"));
    }
    
    private class Order
    {
      public int ID { get; set; }
      public decimal Amount { get; set; }
    }
    
    [Fact]
    public void Message_of_a_custom_object_is_part_of_output()
    {
      var order = new Order
      {
        ID = 999,
        Amount = 10
      };
      
      ShouldFailWith(() => Assert.That(() => order.Amount > 20, order));
    }

    [Fact]
    public void Message_of_a_anonymous_type_is_part_of_output()
    {
      var order = new
      {
        ID = 999,
        Amount = 10
      };

      ShouldFailWith(() => Assert.That(() => order.Amount > 20, order));
    }

    [Fact]
    public void Double_values_use_invariant_culture_in_assertion_output()
    {
      // Save original culture
      var originalCulture = CultureInfo.CurrentCulture;

      try
      {
        // Set culture to German which uses comma as decimal separator
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        double actual = 3.1;

        // This should use invariant culture (period as decimal separator) consistently
        // in both the expression string and the actual/expected values
        // Without the fix, this would show "3,2" with comma in some places
        ShouldFail(
          () => actual == 3.2);
      }
      finally
      {
        CultureInfo.CurrentCulture = originalCulture;
      }
    }

    [Fact]
    public void Decimal_values_use_invariant_culture_in_assertion_output()
    {
      var originalCulture = CultureInfo.CurrentCulture;

      try
      {
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

        decimal amount = 123.45m;

        // Without the fix, French culture would show "999,99" with comma
        ShouldFail(
          () => amount == 999.99m);
      }
      finally
      {
        CultureInfo.CurrentCulture = originalCulture;
      }
    }
  }
}