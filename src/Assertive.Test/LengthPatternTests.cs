using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class LengthPatternTests : AssertionTestBase
  {
    [Fact]
    public void LengthPattern_tests()
    {
      var list = new List<string>
      {
        "a", "b"
      };

      var array = new int[2];

      ShouldFail(() => list.Count == 3, "list should have a Count equal to 3.", "Count: 2.");
      ShouldFail(() => list.Count != 2, "list should have a Count not equal to 2.", "Count: 2.");
      ShouldFail(() => list.Count != array.Length, "list should have a Count not equal to array.Length (value: 2).", "Count: 2.");
      ShouldFail(() => array.Length > 3, "array should have a Length greater than 3.", "Length: 2.");
      ShouldFail(() => list.Count() <= 1, "list should have a Count less than or equal to 1.", "Count: 2.");
      ShouldFail(() => list.Count() > array.Length, "list should have a Count greater than array.Length (value: 2).", "Count: 2.");
    }

    private class Customer
    {
      public int Age { get; set; }
    }

    [Fact]
    public void Count_with_lambda_works()
    {
      var customers = new List<Customer>();
      
      ShouldFail(() => customers.Count(c => c.Age > 50) > 0, "customers with filter c.Age > 50 should have a Count greater than 0.", "Count: 0.");
    }
  }
}