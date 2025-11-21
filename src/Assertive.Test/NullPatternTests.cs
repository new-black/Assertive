using System;
using System.Linq.Expressions;
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
      
      ShouldFail(() => nullString != null, "nullString should not be null.", "null");
      ShouldFail(() => notNullString == null, "notNullString should be null.", @"""a string""");
    }
    
    [Fact]
    public void NullPattern_tests_using_is_object()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString is object, "nullString should not be null.", "null");
      ShouldFail(() => !(notNullString is object), "notNullString should be null.", @"""a string""");
    }
    
    [Fact]
    public void IsDefault_tests()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString != default, "nullString should not be null.", "null");
      ShouldFail(() => nullString != default(string), "nullString should not be null.", "null");
      ShouldFail(() => notNullString == default, "notNullString should be null.", @"""a string""");
      ShouldFail(() => notNullString == default(string), "notNullString should be null.", @"""a string""");
    }

    private static AssertionFailureContext CreateContext(Expression<Func<bool>> assertion)
    {
      return new AssertionFailureContext(new Assertion(assertion, null, null), null);
    }
    
    [Fact]
    public void NullPattern_is_not_triggered_for_default_expression_on_struct()
    {
      DateTime a = DateTime.UtcNow;
      
      var failures = new AssertionFailureAnalyzer(CreateContext(() => a == default)).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && !(failures[0].FriendlyMessagePattern is NullPattern));
    }
    
    [Fact]
    public void NullPattern_is_triggered()
    {
      string notNullString = "a string";
      
      var failures = new AssertionFailureAnalyzer(CreateContext(() => notNullString == null)).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && failures[0].FriendlyMessagePattern is NullPattern);
    }
  }
}