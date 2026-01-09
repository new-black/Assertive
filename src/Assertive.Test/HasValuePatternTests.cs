using Xunit;

namespace Assertive.Test
{
  public class HasValuePatternTests : AssertionTestBase
  {
    [Fact]
    public void HasValue()
    {
      int? a = null;

      ShouldFail(() => a.HasValue, "a should have a value.", "It was null.");
    }
    
    [Fact]
    public void NotHasValue()
    {
      int? a = 1;

      ShouldFail(() => !a.HasValue, "a should not have a value.", "Value: 1.");
    }
  }
}