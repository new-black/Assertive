using Xunit;

namespace Assertive.Test
{
  public class BoolPatternTests : AssertionTestBase
  {
    [Fact]
    public void Test_true()
    {
      var success = false;
      
      ShouldFail(() => success, "Expected success to be true.");
    }
    
    [Fact]
    public void Test_false()
    {
      var success = true;
      ShouldFail(() => !success, "Expected success to be false.");
    }
  }
}