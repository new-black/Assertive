using Xunit;

namespace Assertive.Test
{
  public class StartsWithPatternTests : AssertionTestBase
  {
    [Fact]
    public void StartsWith_constant()
    {
      var myString = "abcdefghijklmnop";
      
      ShouldFail(() => myString.StartsWith("cba"));
    }
    
    [Fact]
    public void NotStartsWith_constant()
    {
      var myString = "abcdefghijklmnop";
      
      ShouldFail(() => !myString.StartsWith("abc"));
    }
    
    [Fact]
    public void StartsWith_variable()
    {
      var myString = "abcdefghijklmnop";
      var prefix = "cba";
      
      ShouldFail(() => myString.StartsWith(prefix));
    }
    
    [Fact]
    public void NotStartsWith_variable()
    {
      var myString = "abcdefghijklmnop";
      var prefix = "abc";
      
      ShouldFail(() => !myString.StartsWith(prefix));
    }
    
    [Fact]
    public void EndsWith_constant()
    {
      var myString = "abcdefghijklmnopabc";
      
      ShouldFail(() => !myString.EndsWith("abc"));
    }

  }
}