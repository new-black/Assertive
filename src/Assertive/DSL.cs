using System;
using System.Linq.Expressions;

namespace Assertive
{
  public static class DSL
  {
    public static void Assert(Expression<Func<bool>> assertion)
    {
      Assertive.Assert.That(assertion);
    }
    
    public static void Assert(Expression<Func<bool>> assertion, object message)
    {
      Assertive.Assert.That(assertion, message);
    }
    
    public static void Assert(Expression<Func<bool>> assertion, Expression<Func<object>> context)
    {
      Assertive.Assert.That(assertion, context);
    }

    public static void Assert(Expression<Func<bool>> assertion, object message, Expression<Func<object>> context)
    {
      Assertive.Assert.That(assertion, message, context);
    }
    
    public static void Throws(Expression<Func<object>> expression)
    {
      Assertive.Assert.Throws(expression);
    }

    public static void Throws(Expression<Action> expression)
    {
      Assertive.Assert.Throws(expression);
    }

    public static void Throws<TException>(Expression<Action> expression) where TException : Exception
    {
      Assertive.Assert.Throws<TException>(expression);
    }
    
    public static void Throws<TException>(Expression<Func<object>> expression) where TException : Exception
    {
      Assertive.Assert.Throws<TException>(expression);
    }
  }
}