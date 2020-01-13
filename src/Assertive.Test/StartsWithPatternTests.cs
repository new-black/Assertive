using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Assertive.Test
{
  public class StartsWithPatternTests : AssertionTestBase
  {
    [Fact]
    public void StartsWith_constant()
    {
      var list = new List<string>()
      {
        "bar"
      };

      Assert.That(() => list.Count == 1 && list[0].StartsWith("foo"));
      
      var myString = "abcdefghijklmnop";
      
      ShouldFail(() => myString.StartsWith("cba"), @"Expected myString to start with ""cba"".

Value of myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void NotStartsWith_constant()
    {
      var myString = "abcdefghijklmnop";
      
      ShouldFail(() => !myString.StartsWith("abc"), @"Expected myString to not start with ""abc"".

Value of myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void StartsWith_variable()
    {
      var myString = "abcdefghijklmnop";
      var prefix = "cba";
      
      ShouldFail(() => myString.StartsWith(prefix), @"Expected myString to start with prefix (value: ""cba"").

Value of myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void NotStartsWith_variable()
    {
      var myString = "abcdefghijklmnop";
      var prefix = "abc";
      
      ShouldFail(() => !myString.StartsWith(prefix), @"Expected myString to not start with prefix (value: ""abc"").

Value of myString: ""abcdefghijklmnop""");
    }
    
    [Fact]
    public void EndsWith_constant()
    {
      var myString = "abcdefghijklmnopabc";
      
      ShouldFail(() => !myString.EndsWith("abc"), @"Expected myString to not end with ""abc"".

Value of myString: ""abcdefghijklmnopabc""");
    }

  }
}