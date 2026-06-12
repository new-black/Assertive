using Xunit;

namespace Assertive.Test
{
  public class HasValuePatternTests : AssertionTestBase
  {
    [Fact]
    public void HasValue()
    {
      int? a = null;

      ShouldFail(() => a.HasValue);
    }
    
    [Fact]
    public void NotHasValue()
    {
      int? a = 1;

      ShouldFail(() => !a.HasValue);
    }
  }
}