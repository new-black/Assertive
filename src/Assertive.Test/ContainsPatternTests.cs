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