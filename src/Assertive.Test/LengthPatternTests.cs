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

      ShouldFail(() => list.Count == 3);
      ShouldFail(() => list.Count != 2);
      ShouldFail(() => list.Count != array.Length);
      ShouldFail(() => array.Length > 3);
      ShouldFail(() => list.Count() <= 1);
      ShouldFail(() => list.Count() > array.Length);
    }

    private class Customer
    {
      public int Age { get; set; }
    }

    [Fact]
    public void Count_with_lambda_works()
    {
      var customers = new List<Customer>();
      
      ShouldFail(() => customers.Count(c => c.Age > 50) > 0);
    }
  }
}