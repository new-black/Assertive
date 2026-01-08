using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Assertive.Config;

namespace Assertive.Test
{
  public class ThrowsTests : AssertionTestBase, IDisposable
  {
    private readonly bool originalColors;

    public ThrowsTests()
    {
      originalColors = Configuration.Colors.Enabled;
    }
    
    public void Dispose()
    {
      Configuration.Colors.Enabled = originalColors;
    }

    [Fact]
    public void Assertion_that_throw_tests()
    {
      StringBuilder? sb = null;
      var array = new int[0];

      ShouldFail(() => sb!.Append("a") != null,
        "NullReferenceException caused by calling Append on sb which was null.");
      ShouldFail(() => array[1] == 1, "IndexOutOfRangeException caused by accessing index 1 on array, actual length was 0.");
    }

    [Fact]
    public async Task Throws_tests()
    {
      StringBuilder? sb = null;
      var array = new int[0];

      Assert.Throws(() => sb!.Append("A"));
      Assert.Throws(() => array[1]);
      Assert.Throws(() => int.Parse("abc"));
      await Assert.Throws(() => ThrowAsyncException());
      
      Assert.Throws<NullReferenceException>(() => sb!.Append("A"));
      Assert.Throws<IndexOutOfRangeException>(() => array[1]);
      Assert.Throws<FormatException>(() => int.Parse("abc"));
      await Assert.Throws<InvalidOperationException>(() => ThrowAsyncException());
    }

    [Fact]
    public async Task Throws_supports_additional_assertion_and_returns_exception()
    {
      var ex = Assert.Throws<InvalidOperationException>(() => ThrowInvalidOperation("boom"), e => e.Message == "boom");
      Xunit.Assert.Equal("boom", ex.Message);

      var baseEx = Assert.Throws(() => ThrowApplicationException("oops"), e => e.GetType() == typeof(ApplicationException) && ((ApplicationException)e).Message == "oops");
      Xunit.Assert.Equal("oops", baseEx.Message);

      var asyncEx = await Assert.Throws<InvalidOperationException>(() => ThrowAsyncException(), e => e.Message.Contains("an exception"));
      Xunit.Assert.Contains("an exception", asyncEx.Message);
    }

    [Fact]
    public void Throws_additional_assertion_failure_is_reported_sync()
    {
      var originalColorSetting = Configuration.Colors.Enabled;
      try
      {
        Configuration.Colors.Enabled = false;
        Assert.Throws<InvalidOperationException>(() => ThrowInvalidOperation("boom"), e => e.Message == "wrong");
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Assert.That(() => StripAnsi(ex.Message).Contains("""
                                                         e.Message == "wrong"
                                                         
                                                          ✓ EXPECTED                                                                     
                                                         e.Message: "wrong"
                                                          ✗ ACTUAL                                                                       
                                                         e.Message: "boom"
                                                         """));
      }
      finally
      {
        Configuration.Colors.Enabled = originalColorSetting;
      }
    }

    [Fact]
    public async Task Throws_additional_assertion_failure_is_reported_async()
    {
      var originalColorSetting = Configuration.Colors.Enabled;
      try
      {
        Configuration.Colors.Enabled = false;
        await Assert.Throws<InvalidOperationException>(() => ThrowAsyncException(), e => e.Message == "wrong");
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Assert.That(() => StripAnsi(ex.Message).Contains("""
                                                         [EXPECTED]
                                                         e.Message: "wrong"
                                                         [ACTUAL]
                                                         e.Message: "an exception"
                                                         String diff (expected vs actual):
                                                         Legend: [E#] expected line, [A#] actual line, plain line number = unchanged
                                                         - [E1] [-wr-]on[-g-]
                                                         + [A1] [+an excepti+]on
                                                         """));
      }
      finally
      {
        Configuration.Colors.Enabled = originalColorSetting;
      }
    }

    [Fact]
    public async Task Failing_Throws_tests()
    {
      StringBuilder sb = new StringBuilder();
      var array = new int[10];

      ShouldThrow(() => sb.Append("A"), @"Expected sb.Append(""A"") to throw an exception, but it did not.");
      ShouldThrow(() => array[1], @"Expected array[1] to throw an exception, but it did not.");
      ShouldThrow(() => int.Parse("123"), @"Expected int.Parse(""123"") to throw an exception, but it did not.");
      await ShouldThrow(() => DontThrowAsync(), @"Expected DontThrowAsync() to throw an exception, but it did not.");

      ShouldThrow<NullReferenceException>(() => sb.Append("A"), @"Expected sb.Append(""A"") to throw an exception, but it did not.");
      ShouldThrow<IndexOutOfRangeException>(() => array[1], @"Expected array[1] to throw an exception, but it did not.");
      ShouldThrow<FormatException>(() => int.Parse("123"), @"Expected int.Parse(""123"") to throw an exception, but it did not.");
      await ShouldThrow<InvalidOperationException>(() => DontThrowAsync(), @"Expected DontThrowAsync() to throw an exception, but it did not.");
    }

    [Fact]
    public async Task Failing_Throws_when_type_mismatch_tests()
    {
      StringBuilder? sb = null;
      var array = new int[0];

      ShouldThrow<InvalidOperationException>(() => sb!.Append("A"), @"Expected sb.Append(""A"") to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.NullReferenceException instead.");
      ShouldThrow<InvalidOperationException>(() => array[1], @"Expected array[1] to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.IndexOutOfRangeException instead.");
      ShouldThrow<InvalidOperationException>(() => int.Parse("abc"), @"Expected int.Parse(""abc"") to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.FormatException instead.");
      await ShouldThrow<NullReferenceException>(() => ThrowAsyncException(), @"Expected ThrowAsyncException() to throw an exception of type System.NullReferenceException, but it threw an exception of type System.InvalidOperationException instead.");
    }

    private async Task ThrowAsyncException()
    {
      await Task.Yield();
      
      throw new InvalidOperationException("an exception");
    }
    
    private async Task DontThrowAsync()
    {
      await Task.Delay(30);
    }

    private static void ThrowInvalidOperation(string message) => throw new InvalidOperationException(message);

    private static void ThrowApplicationException(string message) => throw new ApplicationException(message);
  }
}
