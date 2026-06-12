using System;
using System.Threading.Tasks;
using Assertive.Helpers;

namespace Assertive
{
  internal partial class AssertImpl
  {
    internal readonly struct ThrowsResult
    {
      public ThrowsResult(Exception? failure, Exception? thrown)
      {
        Failure = failure;
        Thrown = thrown;
      }

      public Exception? Failure { get; }
      public Exception? Thrown { get; }
    }

    public static async Task<ThrowsResult> Throws(Func<Task> action, string actionExpression,
      Type? expectedExceptionType = null, Func<Exception, bool>? exceptionAssertion = null, string? exceptionExpression = null,
      Func<object?, int, Exception>? predicateFailure = null)
    {
      var threw = false;
      var expressionBody = GetLambdaBody(actionExpression);

      Exception? thrownException = null;

      try
      {
        await action();
      }
      catch (Exception ex)
      {
        thrownException = ex;
        threw = true;

        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex))
        {
          return new ThrowsResult(ExceptionHelper.GetException(
            $"Expected {expressionBody} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.GetType().FullName} instead."), thrownException);
        }
      }

      if (!threw)
      {
        return new ThrowsResult(ExceptionHelper.GetException($"Expected {expressionBody} to throw an exception, but it did not."), null);
      }

      var assertionFailure = EvaluateExceptionAssertion(exceptionAssertion, exceptionExpression, thrownException!, predicateFailure);

      return new ThrowsResult(assertionFailure, thrownException);
    }

    public static ThrowsResult Throws(Action action, string actionExpression,
      Type? expectedExceptionType = null, Func<Exception, bool>? exceptionAssertion = null, string? exceptionExpression = null,
      Func<object?, int, Exception>? predicateFailure = null)
    {
      var threw = false;
      var expressionBody = GetLambdaBody(actionExpression);
      Exception? thrownException = null;

      try
      {
        action();
      }
      catch (Exception ex)
      {
        thrownException = ex;
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex))
        {
          return new ThrowsResult(ExceptionHelper.GetException(
            $"Expected {expressionBody} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.GetType().FullName} instead."), thrownException);
        }

        threw = true;
      }

      if (!threw)
      {
        return new ThrowsResult(ExceptionHelper.GetException($"Expected {expressionBody} to throw an exception, but it did not."), null);
      }

      var assertionFailure = EvaluateExceptionAssertion(exceptionAssertion, exceptionExpression, thrownException!, predicateFailure);

      return new ThrowsResult(assertionFailure, thrownException);
    }

    private static string GetLambdaBody(string expression)
    {
      // CallerArgumentExpression captures "() => expr" but we want just "expr"
      const string lambdaPrefix = "() => ";
      if (expression.StartsWith(lambdaPrefix))
      {
        expression = expression.Substring(lambdaPrefix.Length);
      }

      return expression;
    }

    private static Exception? EvaluateExceptionAssertion(Func<Exception, bool>? exceptionAssertion, string? exceptionExpression, Exception exception,
      Func<object?, int, Exception>? predicateFailure = null)
    {
      if (exceptionAssertion == null)
      {
        return null;
      }

      var assertionText = exceptionExpression ?? "the exception assertion";

      bool matched;

      try
      {
        matched = exceptionAssertion(exception);
      }
      catch (Exception evaluationException)
      {
        return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
        {
          AssertionText = assertionText,
          Exception = evaluationException,
        });
      }

      if (matched)
      {
        return null;
      }

      // The generated path supplies a factory that decomposes the predicate body with the
      // thrown exception bound to its parameter.
      if (predicateFailure != null)
      {
        try
        {
          return predicateFailure(exception, 0);
        }
        catch
        {
          // Fall through to the plain report.
        }
      }

      return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
      {
        AssertionText = assertionText,
        Exception = exception,
      });
    }
  }
}
