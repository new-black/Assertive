using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Assertive.Runtime
{
  /// <summary>
  /// Infrastructure for code emitted by Assertive's source generator. Not intended to be
  /// called directly from user code.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedAssert
  {
    private static long _interceptedCallCount;

    /// <summary>
    /// The number of Assert.That calls that were served by a generated interceptor in this
    /// process. Exists for diagnostics and for tests that need to prove interception is active.
    /// </summary>
    public static long InterceptedCallCount => Interlocked.Read(ref _interceptedCallCount);

    public static void MarkIntercepted() => Interlocked.Increment(ref _interceptedCallCount);

    /// <summary>
    /// Whether the exception is an Assertive assertion failure (as opposed to an exception
    /// thrown while evaluating the assertion). Used by generated exception filters.
    /// </summary>
    public static bool IsAssertionFailure(Exception exception) => exception.Data.Contains("Assertive.Expected");

    /// <summary>
    /// Extracts the value of a captured local variable or parameter from the assertion
    /// delegate's closure. The compiler stores captured variables as public fields named
    /// after the variable on compiler-generated display classes; captures from multiple
    /// scopes form a chain of display classes, which is searched breadth-first.
    /// </summary>
    public static object? GetCapturedValue(Delegate assertion, string name)
    {
      var target = assertion.Target
        ?? throw new InvalidOperationException($"Assertive: the assertion delegate has no closure to read '{name}' from.");

      if (TryGetCapturedValue(target, name, depth: 0, out var value))
      {
        return value;
      }

      throw new InvalidOperationException($"Assertive: could not locate captured variable '{name}' in the assertion's closure.");
    }

    private static bool TryGetCapturedValue(object closure, string name, int depth, out object? value)
    {
      var type = closure.GetType();
      var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

      if (field != null)
      {
        value = field.GetValue(closure);
        return true;
      }

      if (depth < 4)
      {
        // Captures from enclosing scopes live on chained display-class instances.
        foreach (var chained in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
          if (chained.FieldType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
              && chained.GetValue(closure) is { } next
              && TryGetCapturedValue(next, name, depth + 1, out value))
          {
            return true;
          }
        }
      }

      value = null;
      return false;
    }

    /// <summary>Throws-side entry: a failed (non-exceptional) assertion with no pattern decomposition.</summary>
    public static Exception Failure(string assertionExpression, object? message, Func<object?>? context, string? contextExpression)
    {
      return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
      {
        AssertionText = AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        UserMessage = message,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>An exception thrown while evaluating the assertion.</summary>
    public static Exception EvaluationFailure(string assertionExpression, Exception exception, object? message, Func<object?>? context, string? contextExpression)
    {
      return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
      {
        AssertionText = AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        Exception = exception,
        UserMessage = message,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>A failed ==/!= assertion, decomposed by the generator (EqualsPattern parity).</summary>
    public static Exception EqualityFailure(
      string assertionExpression,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildEquality(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        leftSource,
        leftValue,
        rightSource,
        rightValue,
        rightIsConstant,
        negated,
        locals,
        message,
        context,
        contextExpression);
    }
  }
}
