using System;
using System.Runtime.CompilerServices;
using Assertive.Runtime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Assertive.Test
{
  public abstract partial class AssertionTestBase
  {
    private static partial class AnsiHelper
    {

      [GeneratedRegex(@"\[[0-9;]*[A-Za-z]")]
      public static partial Regex AnsiRegex();

    }
    protected static string StripAnsi(string input)
    {
      var stripped = AnsiHelper.AnsiRegex().Replace(input, "");
      return stripped.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    protected void ShouldThrow(Action action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
    {
      try
      {
        Assert.Throws(action, null, actionExpression);
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: actionExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected Assert.Throws({actionExpression}) to fail but it did not.");
    }

    protected void ShouldThrow(Func<object?> func,
      [CallerArgumentExpression(nameof(func))] string funcExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
    {
      try
      {
        Assert.Throws(func, null, funcExpression);
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: funcExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected Assert.Throws({funcExpression}) to fail but it did not.");
    }

    protected async Task ShouldThrow(Func<Task> action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
    {
      try
      {
        await Assert.Throws(action, null, actionExpression);
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: actionExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected Assert.Throws({actionExpression}) to fail but it did not.");
    }

    protected async Task ShouldThrow<T>(Func<Task> action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0) where T : Exception
    {
      try
      {
        await Assert.Throws<T>(action, null, actionExpression);
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: actionExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected Assert.Throws<{typeof(T).Name}>({actionExpression}) to fail but it did not.");
    }

    protected void ShouldThrow<T>(Action action,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0) where T : Exception
    {
      try
      {
        Assert.Throws<T>(action, null, actionExpression);
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: actionExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected Assert.Throws<{typeof(T).Name}>({actionExpression}) to fail but it did not.");
    }

    protected void ShouldThrow<T>(Func<object?> func,
      [CallerArgumentExpression(nameof(func))] string funcExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0) where T : Exception
    {
      try
      {
        Assert.Throws<T>(func, null, funcExpression);
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: funcExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected Assert.Throws<{typeof(T).Name}>({funcExpression}) to fail but it did not.");
    }

    [AssertionWrapper]
    protected void ShouldFail(Func<bool> assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
      => ShouldFail(AssertionHandle.Degraded(assertion, assertionExpression), assertionExpression, callerFilePath, callerLineNumber);

    internal void ShouldFail(AssertionHandle assertion, string assertionExpression = "", string callerFilePath = "", int callerLineNumber = 0)
    {
      try
      {
        assertion.Assert();
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: assertionExpression,
          sourceFile: callerFilePath);
        return;
      }

      Xunit.Assert.Fail($"Expected assertion to fail but it passed: {assertionExpression}");
    }

    protected void ShouldFailWith(Action assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
    {
      var ex = Xunit.Assert.ThrowsAny<Exception>(assertion);
      Assert.Snapshot(StripAnsi(ex.Message),
        options: $"L{callerLineNumber}",
        expression: assertionExpression,
        sourceFile: callerFilePath);
    }

    protected void SnapshotMessage(Exception ex,
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
    {
      Assert.Snapshot(StripAnsi(ex.Message),
        options: $"L{callerLineNumber}",
        expression: "",
        sourceFile: callerFilePath);
    }
  }
}
