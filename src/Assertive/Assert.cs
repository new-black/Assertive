using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Assertive
{
  /// <summary>
  /// Provides assertion methods for unit testing.
  /// </summary>
  public static class Assert
  {
    /// <summary>
    /// Asserts that the given expression evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean expression to evaluate.</param>
    public static void That(Expression<Func<bool>> assertion)
    {
      var exception = AssertImpl.That(assertion, null, null);

      if (exception != null)
      {
        throw exception;
      }
    }

    /// <summary>
    /// Asserts that the given expression evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean expression to evaluate.</param>
    /// <param name="message">A custom message to include in the failure output.</param>
    public static void That(Expression<Func<bool>> assertion, object message)
    {
      var exception = AssertImpl.That(assertion, message, null);

      if (exception != null)
      {
        throw exception;
      }
    }

    /// <summary>
    /// Asserts that the given expression evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean expression to evaluate.</param>
    /// <param name="context">Additional context to include in the failure output.</param>
    public static void That(Expression<Func<bool>> assertion, Expression<Func<object>> context)
    {
      var exception = AssertImpl.That(assertion, null, context);

      if (exception != null)
      {
        throw exception;
      }
    }

    /// <summary>
    /// Asserts that the given expression evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean expression to evaluate.</param>
    /// <param name="message">A custom message to include in the failure output.</param>
    /// <param name="context">Additional context to include in the failure output.</param>
    public static void That(Expression<Func<bool>> assertion, object message, Expression<Func<object>> context)
    {
      var exception = AssertImpl.That(assertion, message, context);

      if (exception != null)
      {
        throw exception;
      }
    }

    /// <summary>
    /// Asserts that the given expression throws an exception.
    /// </summary>
    /// <param name="expression">An expression that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <returns>The exception that was thrown.</returns>
    public static Exception Throws(Expression<Func<object>> expression, Expression<Func<Exception, bool>>? exceptionAssertion = null)
    {
      var result = AssertImpl.Throws(expression, null, exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given expression throws an exception.
    /// </summary>
    /// <param name="expression">An expression that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <returns>The exception that was thrown.</returns>
    public static Exception Throws(Expression<Action> expression, Expression<Func<Exception, bool>>? exceptionAssertion = null)
    {
      var result = AssertImpl.Throws(expression, null, exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given expression throws an exception of the specified type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="expression">An expression that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <returns>The exception that was thrown.</returns>
    public static TException Throws<TException>(Expression<Action> expression, Expression<Func<TException, bool>>? exceptionAssertion = null) where TException : Exception
    {
      var result = AssertImpl.Throws(expression, typeof(TException), exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given expression throws an exception of the specified type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="expression">An expression that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <returns>The exception that was thrown.</returns>
    public static TException Throws<TException>(Expression<Func<object>> expression, Expression<Func<TException, bool>>? exceptionAssertion = null) where TException : Exception
    {
      var result = AssertImpl.Throws(expression, typeof(TException), exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given async expression throws an exception.
    /// </summary>
    /// <param name="expression">An async expression that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <returns>The exception that was thrown.</returns>
    public static async Task<Exception> Throws(Expression<Func<Task>> expression, Expression<Func<Exception, bool>>? exceptionAssertion = null)
    {
      var result = await AssertImpl.Throws(expression, null, exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given async expression throws an exception of the specified type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="expression">An async expression that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <returns>The exception that was thrown.</returns>
    public static async Task<TException> Throws<TException>(Expression<Func<Task>> expression, Expression<Func<TException, bool>>? exceptionAssertion = null) where TException : Exception
    {
      var result = await AssertImpl.Throws(expression, typeof(TException), exceptionAssertion);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }

    /// <summary>
    /// Asserts that an object matches a previously stored snapshot.
    /// </summary>
    /// <param name="snapshot">The object to compare against the stored snapshot.</param>
    /// <param name="options">Optional settings for the snapshot comparison.</param>
    /// <param name="expression">The expression text (automatically captured).</param>
    /// <param name="sourceFile">The source file path (automatically captured).</param>
    public static void Snapshot(object snapshot, AssertSnapshotOptions? options = null, [CallerArgumentExpression(nameof(snapshot))] string expression = "", [CallerFilePath] string sourceFile = "")
    {
      var exception = AssertImpl.Snapshot(snapshot, options ?? AssertSnapshotOptions.Default, expression, sourceFile);

      if (exception != null)
      {
        throw exception;
      }
    }
  }
}
