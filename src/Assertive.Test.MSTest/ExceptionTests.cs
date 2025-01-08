namespace Assertive.Test.MSTest;

[TestClass]
public class UnitTest1
{
  [TestMethod]
  public void Assert_that_throws_correct_exception_type()
  {
    bool throws = false;
    
    try
    {
      Assert.That(() => false);
    }
    catch (AssertFailedException)
    {
      throws = true;
    }
    
    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(throws);
  }
  
  [TestMethod]
  public void DSL_throws_correct_exception_type()
  {
    bool throws = false;
    
    try
    {
      DSL.Assert(() => false);
    }
    catch (AssertFailedException)
    {
      throws = true;
    }
    
    Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(throws);
  }
}