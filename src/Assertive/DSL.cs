using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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
    
    public static void Assert(object snapshot, AssertSnapshotOptions? options = null, [CallerArgumentExpression(nameof(snapshot))] string expression = "", [CallerFilePath] string sourceFile = "")
    {
      var exception = AssertImpl.Snapshot(snapshot, options ?? AssertSnapshotOptions.Default, expression, sourceFile);

      if (exception != null)
      {
        throw exception;
      }
    }
    
    public static Exception Throws(Expression<Func<object>> expression, Expression<Func<Exception, bool>>? exceptionAssertion = null)
    {
      var result = AssertImpl.Throws(expression, null, exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    public static Exception Throws(Expression<Action> expression, Expression<Func<Exception, bool>>? exceptionAssertion = null)
    {
      var result = AssertImpl.Throws(expression, null, exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    public static TException Throws<TException>(Expression<Action> expression, Expression<Func<TException, bool>>? exceptionAssertion = null) where TException : Exception
    {
      var result = AssertImpl.Throws(expression, typeof(TException), exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }
    
    public static TException Throws<TException>(Expression<Func<object>> expression, Expression<Func<TException, bool>>? exceptionAssertion = null) where TException : Exception
    {
      var result = AssertImpl.Throws(expression, typeof(TException), exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }

    public static async Task<TException> Throws<TException>(Expression<Func<Task>> expression, Expression<Func<TException, bool>>? exceptionAssertion = null) where TException : Exception
    {
      var result = await AssertImpl.Throws(expression, typeof(TException), exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }
    
    public static async Task<Exception> Throws(Expression<Func<Task>> expression, Expression<Func<Exception, bool>>? exceptionAssertion = null)
    {
      var result = await AssertImpl.Throws(expression, null, exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }
  }
}
