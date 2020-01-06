using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Assertive
{
  public static class Assert
  {
    public static void That(Expression<Func<bool>> assertion)
    {
      var compiledAssertion = assertion.Compile(true);

      try
      {
        var result = compiledAssertion();

        if (!result)
        {
          var exceptionProvider = new FailedAssertionExceptionProvider(assertion);

          throw exceptionProvider.GetException();
        }
      }
      catch
      {
        var exceptionProvider = new FailedAssertionExceptionProvider(assertion);

        throw exceptionProvider.GetException();
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
      Type expectedExceptionType = null)
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
        Debug.Assert(ex.InnerException != null, "ex.InnerException != null");

        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex.InnerException))
        {
          throw ExceptionHelper.GetException(
            $"Expected {ExpressionStringBuilder.ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.InnerException.GetType().FullName} instead.");
        }
        
        threw = true;
      }

      if (!threw)
      {
        throw ExceptionHelper.GetException($"Expected {ExpressionStringBuilder.ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }
    }
  }
}