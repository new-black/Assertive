using Xunit;

namespace Assertive.Test
{
  public class HasValuePatternTests : AssertionTestBase
  {
    [Fact]
    public void HasValue()
    {
      int? a = null;

      ShouldFail(() => a.HasValue, "Expected a to have a value.");
    }
    
    [Fact]
    public void NotHasValue()
    {
      int? a = 1;

      ShouldFail(() => !a.HasValue, @"Expected a to not have a value but its value was 1.");
    }
  }
}