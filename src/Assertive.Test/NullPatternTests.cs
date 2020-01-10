using Assertive.Patterns;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class NullPatternTests : AssertionTestBase
  {
    [Fact]
    public void NullPattern_tests()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString != null, "Expected nullString to not be null.");
      ShouldFail(() => notNullString == null, @"Expected notNullString to be null but it was ""a string"" instead.");
    }
    
    [Fact]
    public void NullPattern_is_triggered()
    {
      string notNullString = "a string";
      
      var failures = new AssertionFailureAnalyzer(() => notNullString == null, null).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && failures[0].FriendlyMessagePattern is NullPattern);
    }
  }
}