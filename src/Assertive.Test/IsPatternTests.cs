using Xunit;

namespace Assertive.Test
{
  public class IsPatternTests : AssertionTestBase
  {
    [Fact]
    public void IsPattern_works()
    {
      object o = "foo";
      
      ShouldFail(() => o is int, "Expected o to be of type int but its actual type was string.");
      ShouldFail(() => !(o is string), "Expected o to not be of type string.");

      object? value = null;
      
      ShouldFail(() =>  value is string, "Expected value to be of type string but it was null.");
    }
  }
}