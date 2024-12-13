using NUnit.Framework.Internal;

namespace Assertive.Test.NUnit;

public class Tests
{
  [Test]
  public void Assert_that_throws_correct_exception_type()
  {
    bool throws = false;
    
    try
    {
      Assert.That(() => false);
    }
    catch (AssertionException)
    {
      throws = true;
    }
    
    global::NUnit.Framework.Assert.True(throws);
  }
  
  [Test]
  public void DSL_throws_correct_exception_type()
  {
    bool throws = false;
    
    try
    {
      DSL.Assert(() => false);
    }
    catch (AssertionException)
    {
      throws = true;
    }
    
    global::NUnit.Framework.Assert.True(throws);
  }
}