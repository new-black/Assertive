using Xunit;

namespace Assertive.Test
{
  public class LessThanOrGreaterThanTests : AssertionTestBase
  {
    [Fact]
    public void LessThanOrGreaterThanPattern_tests()
    {
      var one = 1;
      var two = 2;
      
      Assert.That(() => one < two);
      Assert.That(() => one <= two);
      Assert.That(() => two > one);
      Assert.That(() => two >= one);
     
      ShouldFail(() => two < one, "two should be less than one.", @"two: 2
one: 1");
      ShouldFail(() => two <= one, "two should be less than or equal to one.", @"two: 2
one: 1");
      ShouldFail(() => one > two, "one should be greater than two.", @"one: 1
two: 2");
      ShouldFail(() => one >= two, "one should be greater than or equal to two.", @"one: 1
two: 2");
    }

  }
}