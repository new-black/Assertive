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
      
      ShouldFail(() => nullString != null);
      ShouldFail(() => notNullString == null);
    }
    
    [Fact]
    public void NullPattern_tests_using_is_object()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString is object);
      ShouldFail(() => !(notNullString is object));
    }
    
    [Fact]
    public void IsDefault_tests()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString != default);
      ShouldFail(() => nullString != default(string));
      ShouldFail(() => notNullString == default);
      ShouldFail(() => notNullString == default(string));
    }

    [Fact]
    public void Null_message_is_not_used_for_default_expression_on_struct()
    {
      DateTime a = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);

      ShouldFail(() => a == default);
    }
  }
}