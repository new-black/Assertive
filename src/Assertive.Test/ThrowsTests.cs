using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Assertive.Test
{
  public class ThrowsTests : AssertionTestBase
  {
    
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
      await Assert.Throws<InvalidOperationException>(() => ThrowAsyncException_ValueTask());
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

    private async ValueTask ThrowAsyncException_ValueTask()
    {
      await Task.Delay(30);
      
      throw new InvalidOperationException("an exception");
    }

    private async ValueTask<int> ThrowAsyncException_ValueTaskInt()
    {
      await Task.Delay(30);
      
      throw new InvalidOperationException("an exception");
    }
    
    private async Task ThrowAsyncException()
    {
      await Task.Delay(30);
      
      throw new InvalidOperationException("an exception");
    }
    
    private async Task DontThrowAsync()
    {
      await Task.Delay(30);
    }
  }
}