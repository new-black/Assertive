using System;
using System.ComponentModel;
using System.Linq.Expressions;
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
    /// Runs the standard (non-generated) assertion pipeline. Generated interceptors call this
    /// on failure — and on any surprise during captured-value extraction or evaluation — so
    /// that failure messages and exception analysis are identical to the non-intercepted
    /// implementation. Note this re-evaluates the assertion, matching the existing pipeline's
    /// behavior of evaluating during analysis.
    /// </summary>
    public static void Fallback(Expression<Func<bool>> assertion, object? message, Expression<Func<object>>? context)
    {
      var exception = AssertImpl.That(assertion, message, context);

      if (exception != null)
      {
        throw exception;
      }
    }

    /// <summary>
    /// Extracts the value of a captured local variable or parameter from the assertion's
    /// expression tree. The compiler represents captured variables as fields named after the
    /// variable on a compiler-generated closure class, referenced via a ConstantExpression.
    /// Scanning this tree is inherently scope-correct: it only ever sees the closure instances
    /// this specific lambda references.
    /// </summary>
    public static object? GetCapturedValue(LambdaExpression assertion, string name)
    {
      var finder = new CapturedValueFinder(name);

      finder.Visit(assertion.Body);

      if (!finder.Found)
      {
        throw new InvalidOperationException($"Assertive: could not locate captured variable '{name}' in the assertion's expression tree.");
      }

      return finder.Value;
    }

    private sealed class CapturedValueFinder(string name) : ExpressionVisitor
    {
      public bool Found { get; private set; }
      public object? Value { get; private set; }

      protected override Expression VisitMember(MemberExpression node)
      {
        if (!Found
            && node.Member is FieldInfo field
            && field.Name == name
            && node.Expression is ConstantExpression { Value: not null } closure
            && closure.Type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
          Found = true;
          Value = field.GetValue(closure.Value);

          return node;
        }

        return base.VisitMember(node);
      }
    }
  }
}
