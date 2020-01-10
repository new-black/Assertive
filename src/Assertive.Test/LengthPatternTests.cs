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

      ShouldFail(() => list.Count == 3, "Expected list to have a count equal to 3 but the actual count was 2.");
      ShouldFail(() => list.Count != 2, "Expected list to have a count not equal to 2 but they were equal.");
      ShouldFail(() => list.Count != array.Length, "Expected list to have a count not equal to array.Length (value: 2) but they were equal.");
      ShouldFail(() => array.Length > 3, "Expected array to have a length greater than 3 but the actual length was 2.");
      ShouldFail(() => list.Count() <= 1, "Expected list to have a count less than or equal to 1 but the actual count was 2.");
      ShouldFail(() => list.Count() > array.Length, "Expected list to have a count greater than array.Length (value: 2) but the actual count was 2.");
    }

    private class Customer
    {
      public int Age { get; set; }
    }

    [Fact]
    public void Count_with_lambda_works()
    {
      var customers = new List<Customer>();
      
      ShouldFail(() => customers.Count(c => c.Age > 50) > 0, "Expected customers with filter c.Age > 50 to have a count greater than 0 but the actual count was 0.");
    }
  }
}