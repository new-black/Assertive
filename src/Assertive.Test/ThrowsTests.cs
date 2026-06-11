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

      ShouldFail(() => sb!.Append("a") != null);
      ShouldFail(() => array[1] == 1);
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
      var ex = CaptureFailure(() =>
        Assert.Throws<InvalidOperationException>(() => ThrowInvalidOperation("boom"), e => e.Message == "wrong"));
      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Throws_additional_assertion_failure_is_reported_async()
    {
      var ex = await CaptureFailureAsync(() =>
        Assert.Throws<InvalidOperationException>(() => ThrowAsyncException(), e => e.Message == "wrong"));
      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Failing_Throws_tests()
    {
      StringBuilder sb = new StringBuilder();
      var array = new int[10];

      ShouldThrow(() => sb.Append("A"));
      ShouldThrow(() => array[1]);
      ShouldThrow(() => int.Parse("123"));
      await ShouldThrow(() => DontThrowAsync());

      ShouldThrow<NullReferenceException>(() => sb.Append("A"));
      ShouldThrow<IndexOutOfRangeException>(() => array[1]);
      ShouldThrow<FormatException>(() => int.Parse("123"));
      await ShouldThrow<InvalidOperationException>(() => DontThrowAsync());
    }

    [Fact]
    public async Task Failing_Throws_when_type_mismatch_tests()
    {
      StringBuilder? sb = null;
      var array = new int[0];

      ShouldThrow<InvalidOperationException>(() => sb!.Append("A"));
      ShouldThrow<InvalidOperationException>(() => array[1]);
      ShouldThrow<InvalidOperationException>(() => int.Parse("abc"));
      await ShouldThrow<NullReferenceException>(() => ThrowAsyncException());
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
