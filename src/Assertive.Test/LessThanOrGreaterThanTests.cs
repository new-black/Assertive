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
     
      ShouldFail(() => two < one);
      ShouldFail(() => two <= one);
      ShouldFail(() => one > two);
      ShouldFail(() => one >= two);
    }

  }
}