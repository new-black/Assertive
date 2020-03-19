using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Assertive
{
  internal class AssertImpl
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

    public static Exception? Throws(LambdaExpression expression, 
      Type? expectedExceptionType = null)
    {
      var threw = false;

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

      try
      {
        expression.Compile(true).DynamicInvoke();
      }
      catch(TargetInvocationException ex)
      {
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex.InnerException))
        {
          return ExceptionHelper.GetException(
            $"Expected {ExpressionHelper.ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.InnerException.GetType().FullName} instead.");
        }
        
        threw = true;
      }

      if (!threw)
      {
        return ExceptionHelper.GetException($"Expected {ExpressionHelper.ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }

      return null;
    }
  }
}