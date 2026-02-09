using System.Collections.Generic;
using Assertive.Analyzers;
using Assertive.Patterns;
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
      
      ShouldFail(() => list.Contains("d"), @"list should contain ""d"".", @"list: [ ""a"", ""b"", ""c"" ]");
      ShouldFail(() => list.Contains(myValue), @"list should contain myValue (value: ""abc"").", @"list: [ ""a"", ""b"", ""c"" ]");
      ShouldFail(() => list[0].Contains("foo"), @"list[0] should contain the substring ""foo"".", @"list[0]: ""a""");
    }

    [Fact]
    public void ContainsPattern_string_test()
    {
      var value = "abcdefg";
      
      ShouldFail(() => value.Contains("z"), @"value should contain the substring ""z"".", @"value: ""abcdefg""");
    }
    
    [Fact]
    public void Not_ContainsPattern_string_test()
    {
      var value = "abcdefg";
      
      ShouldFail(() => !value.Contains("abc"), @"value should not contain the substring ""abc"".", @"value: ""abcdefg""");
    }
    
    [Fact]
    public void ContainsPattern_string_case_mismatch_hint()
    {
      var value = "Hello World";

      ShouldFail(() => value.Contains("hello world"),
        @"value should contain the substring ""hello world"".",
        @"The strings differ only in casing");
    }

    [Fact]
    public void ContainsPattern_string_newline_mismatch_hint()
    {
      var value = "line1\r\nline2";
      var search = "line1\nline2";

      ShouldFail(() => value.Contains(search),
        @"value should contain the substring search",
        @"String diff (expected vs actual):");
    }

    [Fact]
    public void ContainsPattern_string_no_hint_when_completely_different()
    {
      var value = "abcdefg";

      ShouldFail(() => value.Contains("xyz"),
        @"value should contain the substring ""xyz"".",
        @"value: ""abcdefg""");
    }

    [Fact]
    public void ContainsPattern_string_closest_match_hint()
    {
      var value = "The quick brown fox jumps over the lazy dog";
      
      ShouldFail(() => value.Contains("The quick brown cat jumps over the lazy dog"),
        @"value should contain the substring ""The quick brown cat jumps over the lazy dog"".",
        @"Closest match at position 0 (3 character differences)");
    }

    [Fact]
    public void ContainsPattern_string_closest_match_shows_diff()
    {
      var value = "Hello world, this is a test of the system";

      ShouldFail(() => value.Contains("this is a tast of the"),
        @"value should contain the substring ""this is a tast of the"".",
        @"this is a test of the");
    }

    [Fact]
    public void ContainsPattern_string_closest_match_not_shown_when_too_different()
    {
      var value = "abcdefghij";

      ShouldFail(() => value.Contains("zyxwvutsrq"),
        @"value should contain the substring ""zyxwvutsrq"".",
        @"value: ""abcdefghij""");
    }

    [Fact]
    public void ContainsPattern_is_triggered()
    {
      var list = new List<string>
      {
        "a", "b", "c"
      };

      var failures = new AssertionFailureAnalyzer(new AssertionFailureContext(new Assertion(() => list.Contains("d"), null, null), null)).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && failures[0].FriendlyMessagePattern is ContainsPattern);
    }
  }
}