using Xunit.Sdk;

namespace Assertive.Test.xUnit;

public class UnitTest1
{
  [Fact]
  public void Assert_that_throws_correct_exception_type()
  {
    bool throws = false;
    
    try
    {
      Assert.That(() => false);
    }
    catch (XunitException)
    {
      throws = true;
    }
    
    Xunit.Assert.True(throws);
  }
  
  [Fact]
  public void DSL_throws_correct_exception_type()
  {
    bool throws = false;
    
    try
    {
      DSL.Assert(() => false);
    }
    catch (XunitException)
    {
      throws = true;
    }
    
    Xunit.Assert.True(throws);
  }
}