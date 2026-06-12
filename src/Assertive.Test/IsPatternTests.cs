using Xunit;

namespace Assertive.Test
{
  public class IsPatternTests : AssertionTestBase
  {
    [Fact]
    public void IsPattern_works()
    {
      object o = "foo";
      
      ShouldFail(() => o is int);
      ShouldFail(() => !(o is string));

      object? value = null;
      
      ShouldFail(() =>  value is string);
    }
  }
}