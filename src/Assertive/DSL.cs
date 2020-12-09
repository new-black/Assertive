using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Assertive
{
  public static class DSL
  {
    public static void Assert(Expression<Func<bool>> assertion)
    {
      var exception = AssertImpl.That(assertion, null, null);

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static void Assert(Expression<Func<bool>> assertion, object message)
    {
      var exception = AssertImpl.That(assertion, message, null);

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static void Assert(Expression<Func<bool>> assertion, Expression<Func<object>> context)
    {
      var exception = AssertImpl.That(assertion, null, context);

      if (exception != null)
      {
        throw exception;
      }
    }

    public static void Assert(Expression<Func<bool>> assertion, object message, Expression<Func<object>> context)
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

    public static async Task Throws<TException>(Expression<Func<Task>> expression) where TException : Exception
    {
      var exception = await AssertImpl.Throws(expression, typeof(TException));

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static async Task Throws(Expression<Func<Task>> expression)
    {
      var exception = await AssertImpl.Throws(expression);

      if (exception != null)
      {
        throw exception;
      }
    }
  }
}