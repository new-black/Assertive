using System;
using Assertive.Analyzers;
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
    public void IsDefault_tests()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString != default, "Expected nullString to not be null.");
      ShouldFail(() => nullString != default(string), "Expected nullString to not be null.");
      ShouldFail(() => notNullString == default, @"Expected notNullString to be null but it was ""a string"" instead.");
      ShouldFail(() => notNullString == default(string), @"Expected notNullString to be null but it was ""a string"" instead.");
    }
    
    [Fact]
    public void NullPattern_is_not_triggered_for_default_expression_on_struct()
    {
      DateTime a = DateTime.UtcNow;
      
      var failures = new AssertionFailureAnalyzer(() => a == default(DateTime), null).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && !(failures[0].FriendlyMessagePattern is NullPattern));
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