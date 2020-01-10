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
     
      ShouldFail(() => two < one, "Expected two to be less than one, but two was 2 while one was 1.");
      ShouldFail(() => two <= one, "Expected two to be less than or equal to one, but two was 2 while one was 1.");
      ShouldFail(() => one > two, "Expected one to be greater than two, but one was 1 while two was 2.");
      ShouldFail(() => one >= two, "Expected one to be greater than or equal to two, but one was 1 while two was 2.");
    }

  }
}