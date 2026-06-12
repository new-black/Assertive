using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Assertive.Config;
using Assertive.Runtime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Assertive.Test
{
  public abstract partial class AssertionTestBase
  {
    private static partial class AnsiHelper
    {

      // Match only real ANSI/CSI escape sequences (ESC '[' … letter). The ESC prefix is
      // load-bearing: without it the pattern also eats literal no-color labels like
      // "[EXPECTED]" (which look like "[" + letter), corrupting the snapshot text.
      [GeneratedRegex(@"\u001b\[[0-9;]*[A-Za-z]")]
      public static partial Regex AnsiRegex();

    }
    protected static string StripAnsi(string input)
    {
      var stripped = AnsiHelper.AnsiRegex().Replace(input, "");
      return stripped.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    // Snapshotted failure messages must be byte-for-byte identical regardless of the
    // environment. Colors are auto-enabled when running locally but disabled on CI (and
    // under NUnit, Visual Studio, NO_COLOR, etc.), and the two modes don't just differ by
    // ANSI escape codes — the no-color renderer emits structurally different text (plain
    // [EXPECTED] headers, ASCII diff markers, no padding/icons). To keep snapshots stable
    // we render every captured message with colors turned off, restoring the previous
    // setting afterwards so colour-rendering tests are unaffected.
    private static Exception? CaptureWithColorsDisabled(Action body)
    {
      var original = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        body();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
      finally
      {
        Configuration.Colors.Enabled = original;
      }
    }

    private static async Task<Exception?> CaptureWithColorsDisabledAsync(Func<Task> body)
    {
      var original = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        await body();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
      finally
      {
        Configuration.Colors.Enabled = original;
      }
    }

    /// <summary>
    /// Runs an assertion that is expected to fail, with colorization disabled so the captured
    /// message is deterministic, and returns the resulting exception. Use this instead of
    /// <c>Xunit.Assert.ThrowsAny</c> when the exception is going to be snapshotted via
    /// <see cref="SnapshotMessage"/>.
    /// </summary>
    protected static Exception CaptureFailure(Action assertion)
    {
      var original = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        return Xunit.Assert.ThrowsAny<Exception>(assertion);
      }
      finally
      {
        Configuration.Colors.Enabled = original;
      }
    }

    /// <summary>
    /// Async counterpart to <see cref="CaptureFailure"/>.
    /// </summary>
    protected static async Task<Exception> CaptureFailureAsync(Func<Task> assertion)
    {
      var original = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        return await Xunit.Assert.ThrowsAnyAsync<Exception>(assertion);
      }
      finally
      {
        Configuration.Colors.Enabled = original;
      }
    }

    protected static string HashExpression(string expression)
    {
      return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expression)))[..8];
    }

    private void SnapshotFailure(Exception ex, string expression, string callerFilePath)
    {
      Assert.Snapshot(StripAnsi(ex.Message),
        options: HashExpression(expression),
        expression: expression,
        sourceFile: callerFilePath);
    }

    protected void ShouldThrow(Action action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "")
    {
      var ex = CaptureWithColorsDisabled(() => Assert.Throws(action, null, actionExpression));

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected Assert.Throws({actionExpression}) to fail but it did not.");
      }

      SnapshotFailure(ex!, actionExpression, callerFilePath);
    }

    protected void ShouldThrow(Func<object?> func,
      [CallerArgumentExpression(nameof(func))] string funcExpression = "",
      [CallerFilePath] string callerFilePath = "")
    {
      var ex = CaptureWithColorsDisabled(() => Assert.Throws(func, null, funcExpression));

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected Assert.Throws({funcExpression}) to fail but it did not.");
      }

      SnapshotFailure(ex!, funcExpression, callerFilePath);
    }

    protected async Task ShouldThrow(Func<Task> action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "")
    {
      var ex = await CaptureWithColorsDisabledAsync(() => Assert.Throws(action, null, actionExpression));

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected Assert.Throws({actionExpression}) to fail but it did not.");
      }

      SnapshotFailure(ex!, actionExpression, callerFilePath);
    }

    protected async Task ShouldThrow<T>(Func<Task> action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "") where T : Exception
    {
      var ex = await CaptureWithColorsDisabledAsync(() => Assert.Throws<T>(action, null, actionExpression));

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected Assert.Throws<{typeof(T).Name}>({actionExpression}) to fail but it did not.");
      }

      SnapshotFailure(ex!, actionExpression, callerFilePath);
    }

    protected void ShouldThrow<T>(Action action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "") where T : Exception
    {
      var ex = CaptureWithColorsDisabled(() => Assert.Throws<T>(action, null, actionExpression));

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected Assert.Throws<{typeof(T).Name}>({actionExpression}) to fail but it did not.");
      }

      SnapshotFailure(ex!, actionExpression, callerFilePath);
    }

    protected void ShouldThrow<T>(Func<object?> func,
      [CallerArgumentExpression(nameof(func))] string funcExpression = "",
      [CallerFilePath] string callerFilePath = "") where T : Exception
    {
      var ex = CaptureWithColorsDisabled(() => Assert.Throws<T>(func, null, funcExpression));

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected Assert.Throws<{typeof(T).Name}>({funcExpression}) to fail but it did not.");
      }

      SnapshotFailure(ex!, funcExpression, callerFilePath);
    }

    [AssertionWrapper]
    protected void ShouldFail(Func<bool> assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerFilePath] string callerFilePath = "")
      => ShouldFail(AssertionHandle.Degraded(assertion, assertionExpression), assertionExpression, callerFilePath);

    internal void ShouldFail(AssertionHandle assertion, string assertionExpression = "", string callerFilePath = "")
    {
      var ex = CaptureWithColorsDisabled(() => assertion.Assert());

      if (ex == null)
      {
        Xunit.Assert.Fail($"Expected assertion to fail but it passed: {assertionExpression}");
      }

      SnapshotFailure(ex!, assertionExpression, callerFilePath);
    }

    protected void ShouldFailWith(Action assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerFilePath] string callerFilePath = "")
    {
      var ex = CaptureFailure(assertion);
      SnapshotFailure(ex, assertionExpression, callerFilePath);
    }

    protected void SnapshotMessage(Exception ex,
      [CallerFilePath] string callerFilePath = "")
    {
      Assert.Snapshot(StripAnsi(ex.Message),
        expression: "",
        sourceFile: callerFilePath);
    }
  }
}
