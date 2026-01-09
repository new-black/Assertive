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
  
  [Test]
  public void Colors_are_disabled_by_default()
  {
    DSL.Assert(() => !Config.Configuration.Colors.Enabled);

    var expected = "abc";
    var actual = "def";

    try
    {
      DSL.Assert(() => expected == actual);
      global::NUnit.Framework.Assert.Fail();
    }
    catch(Exception ex)
    {
      DSL.Assert(() => ex.Message.Contains("""
                                            [EXPECTED]
                                            expected: "def"
                                            [ACTUAL]
                                            expected: "abc"
                                            """));
    }
  }
}