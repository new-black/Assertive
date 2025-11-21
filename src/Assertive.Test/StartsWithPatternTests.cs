using Xunit;

namespace Assertive.Test
{
  public class StartsWithPatternTests : AssertionTestBase
  {
    [Fact]
    public void StartsWith_constant()
    {
      var myString = "abcdefghijklmnop";
      
      ShouldFail(() => myString.StartsWith("cba"), @"myString: should start with ""cba"".", @"myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void NotStartsWith_constant()
    {
      var myString = "abcdefghijklmnop";
      
      ShouldFail(() => !myString.StartsWith("abc"), @"myString: should not start with ""abc"".", @"myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void StartsWith_variable()
    {
      var myString = "abcdefghijklmnop";
      var prefix = "cba";
      
      ShouldFail(() => myString.StartsWith(prefix), @"myString: should start with prefix.

prefix: ""cba""", @"myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void NotStartsWith_variable()
    {
      var myString = "abcdefghijklmnop";
      var prefix = "abc";
      
      ShouldFail(() => !myString.StartsWith(prefix), @"myString: should not start with prefix.

prefix: ""abc""", @"myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void EndsWith_constant()
    {
      var myString = "abcdefghijklmnopabc";
      
      ShouldFail(() => !myString.EndsWith("abc"), @"myString: should not end with ""abc"".", @"myString: ""abcdefghijklmnopabc""");
    }

  }
}