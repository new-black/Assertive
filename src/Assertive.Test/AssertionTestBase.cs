using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Assertive.Test
{
  public abstract class AssertionTestBase
  {
    protected void ShouldThrow(Expression<Func<object>> assertion, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.Throws(assertion);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;

        Assert.That(() => ex.Message.StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected async Task ShouldThrow(Expression<Func<Task>> assertion, string expectedMessage)
    {
      bool throws = false;

      try
      {
        await Assert.Throws(assertion);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;

        Assert.That(() => ex.Message.StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected async Task ShouldThrow<T>(Expression<Func<Task>> assertion, string expectedMessage) where T : Exception
    {
      bool throws = false;

      try
      {
        await Assert.Throws<T>(assertion);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => ex.Message.StartsWith(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldThrow<T>(Expression<Func<object>> assertion, string expectedMessage) where T : Exception
    {
      bool throws = false;

      try
      {
        Assert.Throws<T>(assertion);
        Xunit.Assert.Fail("Should have thrown");
      }
      catch (Exception ex)
      {
        throws = true;
        Assert.That(() => ex.Message.StartsWith(expectedMessage));
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
        throw;
        var expected = string.Join(Environment.NewLine, ex.Data["Assertive.Expected"] as string[]);
        var actual = string.Join(Environment.NewLine, ex.Data["Assertive.Actual"] as string[]);
        var handledExceptions = string.Join(Environment.NewLine, ex.Data["Assertive.HandledExceptions"] as string[]);

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
        Assert.That(() => ex.Message.Contains(expectedMessage));
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
        Assert.That(() => ex.Message.Contains(expectedMessage));
      }

      Xunit.Assert.True(throws);
    }
  }
}