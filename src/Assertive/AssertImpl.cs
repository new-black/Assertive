using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Assertive.Analyzers;
using Assertive.Helpers;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive
{
  internal partial class AssertImpl
  {
    public static Exception? That(Expression<Func<bool>> assertion, object? message, Expression<Func<object>>? context)
    {
      var compiledAssertion = assertion.Compile(true);

      Exception? exceptionToThrow = null;

      try
      {
        var result = compiledAssertion();

        if (!result)
        {
          var exceptionProvider = new FailedAssertionExceptionProvider(new Assertion(assertion, message, context));

          exceptionToThrow = exceptionProvider.GetException();
        }
      }
      catch (Exception ex)
      {
        var exceptionProvider = new FailedAssertionExceptionProvider(new Assertion(assertion, message, context));

        exceptionToThrow = exceptionProvider.GetException(ex);
      }

      return exceptionToThrow;
    }

    public static async Task<Exception?> Throws(Expression<Func<Task>> expression,
      Type? expectedExceptionType = null)
    {
      var threw = false;

      var bodyExpression = GetBodyExpression(expression);

      try
      {
        var task = (Task)expression.Compile(true).DynamicInvoke()!;

        await task;
      }
      catch (Exception ex)
      {
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex))
        {
          return ExceptionHelper.GetException(
            $"Expected {ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.GetType().FullName} instead.");
        }

        threw = true;
      }

      if (!threw)
      {
        return ExceptionHelper.GetException($"Expected {ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }

      return null;
    }

    public static Exception? Throws(LambdaExpression expression,
      Type? expectedExceptionType = null)
    {
      var threw = false;

      var bodyExpression = GetBodyExpression(expression);

      try
      {
        expression.Compile(true).DynamicInvoke();
      }
      catch (TargetInvocationException ex)
      {
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex.InnerException))
        {
          return ExceptionHelper.GetException(
            $"Expected {ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.InnerException!.GetType().FullName} instead.");
        }

        threw = true;
      }

      if (!threw)
      {
        return ExceptionHelper.GetException($"Expected {ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }

      return null;
    }

    private static Expression GetBodyExpression(LambdaExpression expression)
    {
      Expression bodyExpression;

      if (expression.Body.NodeType == ExpressionType.Convert
          && expression.Body is UnaryExpression convertExpression
          && expression.Body.Type == typeof(object))
      {
        bodyExpression = convertExpression.Operand;
      }
      else
      {
        bodyExpression = expression.Body;
      }

      return bodyExpression;
    }
  }
}