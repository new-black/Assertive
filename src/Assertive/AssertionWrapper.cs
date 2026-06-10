using System;
using System.ComponentModel;
using System.Linq.Expressions;

namespace Assertive
{
  /// <summary>
  /// Marks a method as an assertion wrapper: a method that takes an assertion as its first
  /// parameter and forwards it to Assertive. The source generator intercepts call sites of
  /// the marked method where the assertion is a lambda literal and routes them to the
  /// overload of the same method that takes an <see cref="AssertionHandle"/>, carrying a
  /// generated evaluator for the assertion.
  ///
  /// A wrapper is a pair of overloads:
  /// <code>
  /// [AssertionWrapper]
  /// void ShouldFail(Expression&lt;Func&lt;bool&gt;&gt; assertion, string expected)
  ///   =&gt; ShouldFail(AssertionHandle.Degraded(assertion), expected);
  ///
  /// internal void ShouldFail(AssertionHandle assertion, string expected) { ... }
  /// </code>
  /// The handle overload must be accessible from generated code (internal or public, not
  /// private/protected), since interceptors are emitted outside the declaring type.
  /// </summary>
  [AttributeUsage(AttributeTargets.Method)]
  public sealed class AssertionWrapperAttribute : Attribute
  {
  }

  /// <summary>
  /// An assertion as received by the handle overload of an assertion wrapper pair
  /// (see <see cref="AssertionWrapperAttribute"/>). Pass it to
  /// <see cref="Assert.That(AssertionHandle)"/> to evaluate it.
  /// </summary>
  public readonly struct AssertionHandle
  {
    private AssertionHandle(Expression<Func<bool>> assertion, Func<bool>? generatedEvaluator)
    {
      Assertion = assertion;
      GeneratedEvaluator = generatedEvaluator;
    }

    internal Expression<Func<bool>> Assertion { get; }

    /// <summary>Evaluates the assertion without compiling the expression; null when degraded.</summary>
    internal Func<bool>? GeneratedEvaluator { get; }

    /// <summary>
    /// Creates a handle that evaluates through the standard expression pipeline. This is
    /// what the wrapper's sugar overload uses; it is the behavior of call sites that the
    /// generator does not intercept.
    /// </summary>
    public static AssertionHandle Degraded(Expression<Func<bool>> assertion) => new(assertion, null);

    /// <summary>Infrastructure for generated code. Not intended for direct use.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static AssertionHandle Generated(Expression<Func<bool>> assertion, Func<bool> evaluator) => new(assertion, evaluator);
  }
}
