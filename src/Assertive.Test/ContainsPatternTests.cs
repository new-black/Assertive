using System.Collections.Generic;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class ContainsPatternTests : AssertionTestBase
  {
    [Fact]
    public void ContainsPattern_tests()
    {
      var list = new List<string>
      {
        "a", "b", "c"
      };

      var myValue = "abc";
      
      ShouldFail(() => list.Contains("d"));
      ShouldFail(() => list.Contains(myValue));
      ShouldFail(() => list[0].Contains("foo"));
    }

    [Fact]
    public void ContainsPattern_string_test()
    {
      var value = "abcdefg";
      
      ShouldFail(() => value.Contains("z"));
    }
    
    [Fact]
    public void Not_ContainsPattern_string_test()
    {
      var value = "abcdefg";
      
      ShouldFail(() => !value.Contains("abc"));
    }
    
    [Fact]
    public void ContainsPattern_string_case_mismatch_hint()
    {
      var value = "Hello World";

      ShouldFail(() => value.Contains("hello world"));
    }

    [Fact]
    public void ContainsPattern_string_newline_mismatch_hint()
    {
      var value = "line1\r\nline2";
      var search = "line1\nline2";

      ShouldFail(() => value.Contains(search));
    }

    [Fact]
    public void ContainsPattern_string_no_hint_when_completely_different()
    {
      var value = "abcdefg";

      ShouldFail(() => value.Contains("xyz"));
    }

    [Fact]
    public void ContainsPattern_string_closest_match_hint()
    {
      var value = "The quick brown fox jumps over the lazy dog";
      
      ShouldFail(() => value.Contains("The quick brown cat jumps over the lazy dog"));
    }

    [Fact]
    public void ContainsPattern_string_closest_match_shows_diff()
    {
      var value = "Hello world, this is a test of the system";

      ShouldFail(() => value.Contains("this is a tast of the"));
    }

    [Fact]
    public void ContainsPattern_string_closest_match_not_shown_when_too_different()
    {
      var value = "abcdefghij";

      ShouldFail(() => value.Contains("zyxwvutsrq"));
    }
  }
}