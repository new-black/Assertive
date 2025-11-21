using Xunit;

namespace Assertive.Test
{
  public class IsPatternTests : AssertionTestBase
  {
    [Fact]
    public void IsPattern_works()
    {
      object o = "foo";
      
      ShouldFail(() => o is int, "o should be of type int.", "Type: string.");
      ShouldFail(() => !(o is string), "o should not be of type string.", "Type: string.");

      object? value = null;
      
      ShouldFail(() =>  value is string, "value should be of type string.", "It was null.");
    }
  }
}