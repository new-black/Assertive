using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Assertive
{
  /// <summary>
  /// Provides assertion methods for unit testing.
  /// </summary>
  [System.Diagnostics.StackTraceHidden]
  public static class Assert
  {
    /// <summary>
    /// The assertion source text shown when a call site was not intercepted by the source
    /// generator (stored delegate, named arguments, generator not running): the interceptor
    /// embeds the source text; without it there is nothing to report.
    /// </summary>
    internal const string UninterceptedSource = "(assertion source unavailable: this call site was not intercepted by the Assertive source generator)";

    /// <summary>
    /// Asserts that the given condition evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean condition to evaluate.</param>
    /// <param name="message">A custom message to include in the failure output.</param>
    /// <param name="context">Additional context to include in the failure output.</param>
    public static void That(Func<bool> assertion, object? message = null, Func<object?>? context = null)
    {
      ThatCore(assertion, message, context, UninterceptedSource, null);
    }

    /// <summary>
    /// Asserts that the given condition evaluates to true.
    /// </summary>
    /// <param name="assertion">A boolean condition to evaluate.</param>
    /// <param name="context">Additional context to include in the failure output.</param>
    public static void That(Func<bool> assertion, Func<object?> context)
    {
      ThatCore(assertion, null, context, UninterceptedSource, null);
    }

    internal static void ThatCore(Func<bool> assertion, object? message, Func<object?>? context, string assertionExpression, string? contextExpression)
    {
      bool passed;

      try
      {
        passed = assertion();
      }
      catch (Exception ex) when (!Runtime.GeneratedAssert.IsAssertionFailure(ex))
      {
        throw Runtime.GeneratedAssert.EvaluationFailure(assertionExpression, ex, null, null, message, context, contextExpression);
      }

      if (!passed)
      {
        throw Runtime.GeneratedAssert.Failure(assertionExpression, null, message, context, contextExpression);
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
      var result = AssertImpl.Throws(action, actionExpression, typeof(TException), Wrap(exceptionAssertion), exceptionExpression);

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
      var result = AssertImpl.Throws(() => { _ = func(); }, funcExpression, typeof(TException), Wrap(exceptionAssertion), exceptionExpression);

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
      var result = await AssertImpl.Throws(action, actionExpression, typeof(TException), Wrap(exceptionAssertion), exceptionExpression);

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

    internal static Func<Exception, bool>? Wrap<TException>(Func<TException, bool>? exceptionAssertion) where TException : Exception
    {
      return exceptionAssertion == null ? null : ex => exceptionAssertion((TException)ex);
    }
  }
}
