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
      
      ShouldFail(() => list.Contains("d"), @"Expected list to contain ""d"".");
      ShouldFail(() => list.Contains(myValue), @"Expected list to contain myValue (value: ""abc"").");
      ShouldFail(() => list[0].Contains("foo"), @"Expected list[0] (value: ""a"") to contain ""foo"".");
    }

    [Fact]
    public void ContainsPattern_string_test()
    {
      var value = "abcdefg";
      
      ShouldFail(() => value.Contains("z"), @"Expected value (value: ""abcdefg"") to contain ""z"".");
    }
    
    [Fact]
    public void Not_ContainsPattern_string_test()
    {
      var value = "abcdefg";
      
      ShouldFail(() => !value.Contains("abc"), @"Expected value (value: ""abcdefg"") to not contain ""abc"".");
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