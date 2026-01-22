using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Assertive.Test
{
  public abstract partial class AssertionTestBase
  {
    private static partial class AnsiHelper
    {

      [GeneratedRegex(@"\u001b\[[0-9;]*[A-Za-z]")]
      public static partial Regex AnsiRegex();

    }
    protected static string StripAnsi(string input)
    {
      return AnsiHelper.AnsiRegex().Replace(input, "");
    }

    protected void ShouldThrow(Action action, string expectedMessage, [CallerArgumentExpression(nameof(action))] string actionExpression = "")
    {
      bool throws = false;

      try
      {
        Assert.Throws(action, null, actionExpression);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;

        Assert.That(() => StripAnsi(ex.Message).StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldThrow(Func<object?> func, string expectedMessage, [CallerArgumentExpression(nameof(func))] string funcExpression = "")
    {
      bool throws = false;

      try
      {
        Assert.Throws(func, null, funcExpression);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;

        Assert.That(() => StripAnsi(ex.Message).StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected async Task ShouldThrow(Func<Task> action, string expectedMessage, [CallerArgumentExpression(nameof(action))] string actionExpression = "")
    {
      bool throws = false;

      try
      {
        await Assert.Throws(action, null, actionExpression);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;

        Assert.That(() => StripAnsi(ex.Message).StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected async Task ShouldThrow<T>(Func<Task> action, string expectedMessage, [CallerArgumentExpression(nameof(action))] string actionExpression = "") where T : Exception
    {
      bool throws = false;

      try
      {
        await Assert.Throws<T>(action, null, actionExpression);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => StripAnsi(ex.Message).StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldThrow<T>(Action action, string expectedMessage, [CallerArgumentExpression(nameof(action))] string actionExpression = "") where T : Exception
    {
      bool throws = false;

      try
      {
        Assert.Throws<T>(action, null, actionExpression);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => StripAnsi(ex.Message).StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldThrow<T>(Func<object?> func, string expectedMessage, [CallerArgumentExpression(nameof(func))] string funcExpression = "") where T : Exception
    {
      bool throws = false;

      try
      {
        Assert.Throws<T>(func, null, funcExpression);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => StripAnsi(ex.Message).StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldFail(Expression<Func<bool>> assertion, string expectedMessage, string actualMessage = null, bool exactMatch = false)
    {
      bool throws = false;

      try
      {
        Assert.That(assertion);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        var expected = StripAnsi(string.Join(Environment.NewLine, ex.Data["Assertive.Expected"] as string[]));
        var actual = StripAnsi(string.Join(Environment.NewLine, ex.Data["Assertive.Actual"] as string[]));
        var handledExceptions = StripAnsi(string.Join(Environment.NewLine, ex.Data["Assertive.HandledExceptions"] as string[]));

        if (handledExceptions != "")
        {
          Assert.That(() => handledExceptions.Trim() == expectedMessage.Trim());
          return;
        }
        else if (actualMessage == null)
        {
          var expectedPrefix = "";
          var actualPrefix = "";

          if (expected.Contains("\"") || expected.Contains("\n"))
          {
            expectedPrefix = "@";
            expected = expected.Replace("\"", "\"\"");
          }

          if (actual.Contains("\"") || actual.Contains("\n"))
          {
            actualPrefix = "@";
            actual = actual.Replace("\"", "\"\"");
          }

          var message = $"""
                         {expectedPrefix}"{expected}", {actualPrefix}"{actual}"
                         """;


          throw new Exception(message);
        }

        //throw;
        throws = true;

        if (exactMatch)
        {
          Assert.That(() => expected == expectedMessage);
          Assert.That(() => actual == actualMessage);
        }
        else
        {
          Assert.That(() => expected.Contains(expectedMessage));
          Assert.That(() => actual.Contains(actualMessage));
        }
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldFail(Expression<Func<bool>> assertion, Expression<Func<object>> context, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.That(assertion, context);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => StripAnsi(ex.Message).Contains(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldFailWithMessage(Expression<Func<bool>> assertion, object message, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.That(assertion, message);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => StripAnsi(ex.Message).Contains(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }
  }
}