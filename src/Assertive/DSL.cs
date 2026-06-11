using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Assertive
{
  /// <summary>
  /// Provides assertion methods for use with <c>using static Assertive.DSL</c>.
  /// This allows writing assertions without a class prefix, e.g., <c>Assert(() => x == y)</c>.
  /// </summary>
  public static class DSL
  {
    /// <summary>
    /// Asserts that the given condition evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean condition to evaluate.</param>
    /// <param name="message">A custom message to include in the failure output.</param>
    /// <param name="context">Additional context to include in the failure output.</param>
    /// <param name="assertionExpression">The assertion source text (automatically captured).</param>
    /// <param name="contextExpression">The context source text (automatically captured).</param>
    public static void Assert(Func<bool> assertion, object? message = null, Func<object?>? context = null,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerArgumentExpression(nameof(context))] string? contextExpression = null)
    {
      Assertive.Assert.ThatCore(assertion, message, context, assertionExpression, contextExpression);
    }

    /// <summary>
    /// Asserts that the given condition evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean condition to evaluate.</param>
    /// <param name="context">Additional context to include in the failure output.</param>
    /// <param name="assertionExpression">The assertion source text (automatically captured).</param>
    /// <param name="contextExpression">The context source text (automatically captured).</param>
    public static void Assert(Func<bool> assertion, Func<object?> context,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerArgumentExpression(nameof(context))] string? contextExpression = null)
    {
      Assertive.Assert.ThatCore(assertion, null, context, assertionExpression, contextExpression);
    }

    /// <summary>
    /// Asserts that an object matches a previously stored snapshot.
    /// </summary>
    /// <param name="snapshot">The object to compare against the stored snapshot.</param>
    /// <param name="options">Optional settings for the snapshot comparison.</param>
    /// <param name="expression">The expression text (automatically captured).</param>
    /// <param name="sourceFile">The source file path (automatically captured).</param>
    public static void Assert(object snapshot, AssertSnapshotOptions? options = null, [CallerArgumentExpression(nameof(snapshot))] string expression = "", [CallerFilePath] string sourceFile = "")
    {
      var exception = AssertImpl.Snapshot(snapshot, options ?? AssertSnapshotOptions.Default, expression, sourceFile);

      if (exception != null)
      {
        throw exception;
      }
    }

    /// <summary>
    /// Asserts that the given action throws an exception.
    /// </summary>
    /// <param name="action">An action that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <param name="actionExpression">The action expression text (automatically captured).</param>
    /// <param name="exceptionExpression">The predicate source text (automatically captured).</param>
    /// <returns>The exception that was thrown.</returns>
    public static Exception Throws(Action action, Func<Exception, bool>? exceptionAssertion = null,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null)
    {
      var result = AssertImpl.Throws(action, actionExpression, null, exceptionAssertion, exceptionExpression);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given action throws an exception of the specified type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">An action that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <param name="actionExpression">The action expression text (automatically captured).</param>
    /// <param name="exceptionExpression">The predicate source text (automatically captured).</param>
    /// <returns>The exception that was thrown.</returns>
    public static TException Throws<TException>(Action action, Func<TException, bool>? exceptionAssertion = null,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null) where TException : Exception
    {
      var result = AssertImpl.Throws(action, actionExpression, typeof(TException), Assertive.Assert.Wrap(exceptionAssertion), exceptionExpression);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given function throws an exception.
    /// </summary>
    /// <param name="func">A function that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <param name="funcExpression">The function expression text (automatically captured).</param>
    /// <param name="exceptionExpression">The predicate source text (automatically captured).</param>
    /// <returns>The exception that was thrown.</returns>
    public static Exception Throws(Func<object?> func, Func<Exception, bool>? exceptionAssertion = null,
      [CallerArgumentExpression(nameof(func))] string funcExpression = "",
      [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null)
    {
      var result = AssertImpl.Throws(() => { _ = func(); }, funcExpression, null, exceptionAssertion, exceptionExpression);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given function throws an exception of the specified type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="func">A function that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <param name="funcExpression">The function expression text (automatically captured).</param>
    /// <param name="exceptionExpression">The predicate source text (automatically captured).</param>
    /// <returns>The exception that was thrown.</returns>
    public static TException Throws<TException>(Func<object?> func, Func<TException, bool>? exceptionAssertion = null,
      [CallerArgumentExpression(nameof(func))] string funcExpression = "",
      [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null) where TException : Exception
    {
      var result = AssertImpl.Throws(() => { _ = func(); }, funcExpression, typeof(TException), Assertive.Assert.Wrap(exceptionAssertion), exceptionExpression);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given async action throws an exception.
    /// </summary>
    /// <param name="action">An async action that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <param name="actionExpression">The action expression text (automatically captured).</param>
    /// <param name="exceptionExpression">The predicate source text (automatically captured).</param>
    /// <returns>The exception that was thrown.</returns>
    public static async Task<Exception> Throws(Func<Task> action, Func<Exception, bool>? exceptionAssertion = null,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null)
    {
      var result = await AssertImpl.Throws(action, actionExpression, null, exceptionAssertion, exceptionExpression);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Asserts that the given async action throws an exception of the specified type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">An async action that should throw an exception.</param>
    /// <param name="exceptionAssertion">An optional predicate to validate the thrown exception.</param>
    /// <param name="actionExpression">The action expression text (automatically captured).</param>
    /// <param name="exceptionExpression">The predicate source text (automatically captured).</param>
    /// <returns>The exception that was thrown.</returns>
    public static async Task<TException> Throws<TException>(Func<Task> action, Func<TException, bool>? exceptionAssertion = null,
      [CallerArgumentExpression(nameof(action))] string actionExpression = "",
      [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null) where TException : Exception
    {
      var result = await AssertImpl.Throws(action, actionExpression, typeof(TException), Assertive.Assert.Wrap(exceptionAssertion), exceptionExpression);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return (TException)result.Thrown!;
    }
  }
}
