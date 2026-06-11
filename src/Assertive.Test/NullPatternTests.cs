using System;
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

    [Fact]
    public void Null_message_is_not_used_for_default_expression_on_struct()
    {
      DateTime a = DateTime.UtcNow;

      try
      {
        Assert(() => a == default);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        // default on a struct is a value comparison, not a null check.
        Xunit.Assert.DoesNotContain("should be null", StripAnsi(ex.Message));
        Xunit.Assert.Contains("a: ", StripAnsi(ex.Message));
      }
    }
  }
}