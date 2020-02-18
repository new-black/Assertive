using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Assertive
{
  public static class Assert
  {
    public static void That(Expression<Func<bool>> assertion)
    {
      ThatImpl(assertion, null, null);
    }

    public static void That(Expression<Func<bool>> assertion, object message)
    {
      ThatImpl(assertion, message, null);
    }
    
    public static void That(Expression<Func<bool>> assertion, Expression<Func<object>> context)
    {
      ThatImpl(assertion, null, context);
    }

    public static void That(Expression<Func<bool>> assertion, object message, Expression<Func<object>> context)
    {
      ThatImpl(assertion, message, context);
    }
    
    private static void ThatImpl(Expression<Func<bool>> assertion, object? message, Expression<Func<object>>? context)
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

      if (exceptionToThrow != null)
      {
        throw exceptionToThrow;
      }
    }

    public static void Throws(Expression<Func<object>> expression)
    {
      ThrowsImpl(expression);
    }

    public static void Throws(Expression<Action> expression)
    {
      ThrowsImpl(expression);
    }

    public static void Throws<TException>(Expression<Action> expression) where TException : Exception
    {
      ThrowsImpl(expression, typeof(TException));
    }
    
    public static void Throws<TException>(Expression<Func<object>> expression) where TException : Exception
    {
      ThrowsImpl(expression, typeof(TException));
    }
    
    private static void ThrowsImpl(LambdaExpression expression, 
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
          throw ExceptionHelper.GetException(
            $"Expected {ExpressionHelper.ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.InnerException.GetType().FullName} instead.");
        }
        
        threw = true;
      }

      if (!threw)
      {
        throw ExceptionHelper.GetException($"Expected {ExpressionHelper.ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }
    }
  }
}