using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Assertive
{
  public static class Assert
  {
    public static void That(Expression<Func<bool>> assertion)
    {
      var exception = AssertImpl.That(assertion, null, null);

      if (exception != null)
      {
        throw exception;
      }
    }

    public static void That(Expression<Func<bool>> assertion, object message)
    {
      var exception = AssertImpl.That(assertion, message, null);

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static void That(Expression<Func<bool>> assertion, Expression<Func<object>> context)
    {
      var exception = AssertImpl.That(assertion, null, context);

      if (exception != null)
      {
        throw exception;
      }
    }

    public static void That(Expression<Func<bool>> assertion, object message, Expression<Func<object>> context)
    {
      var exception = AssertImpl.That(assertion, message, context);

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static void Throws(Expression<Func<object>> expression)
    {
      var exception = AssertImpl.Throws(expression);

      if (exception != null)
      {
        throw exception;
      }
    }

    public static void Throws(Expression<Action> expression)
    {
      var exception = AssertImpl.Throws(expression);

      if (exception != null)
      {
        throw exception;
      }
    }

    public static void Throws<TException>(Expression<Action> expression) where TException : Exception
    {
      var exception = AssertImpl.Throws(expression, typeof(TException));

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static void Throws<TException>(Expression<Func<object>> expression) where TException : Exception
    {
      var exception = AssertImpl.Throws(expression, typeof(TException));

      if (exception != null)
      {
        throw exception;
      }
    }
  }
}