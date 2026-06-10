using System;
using System.ComponentModel;

namespace Assertive
{
  /// <summary>
  /// Marks a method as an assertion wrapper: a method that takes an assertion as its first
  /// parameter and forwards it to Assertive. The source generator intercepts call sites of
  /// the marked method where the assertion is a lambda literal and routes them to the
  /// overload of the same method that takes an <see cref="AssertionHandle"/>, carrying a
  /// generated assertion for full decomposition.
  ///
  /// A wrapper is a pair of overloads:
  /// <code>
  /// [AssertionWrapper]
  /// void ShouldFail(Func&lt;bool&gt; assertion, string expected,
  ///     [CallerArgumentExpression(nameof(assertion))] string expr = "")
  ///   =&gt; ShouldFail(AssertionHandle.Degraded(assertion, expr), expected, expr);
  ///
  /// internal void ShouldFail(AssertionHandle assertion, string expected, string expr = "") { ... }
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
    private AssertionHandle(Func<bool>? condition, string? sourceText, Action? generatedAssertion)
    {
      Condition = condition;
      SourceText = sourceText;
      GeneratedAssertion = generatedAssertion;
    }

    internal Func<bool>? Condition { get; }
    internal string? SourceText { get; }

    /// <summary>Performs the full generated assertion (evaluates and throws on failure); null when degraded.</summary>
    internal Action? GeneratedAssertion { get; }

    /// <summary>
    /// Creates a handle that evaluates the plain delegate and reports failures from the
    /// assertion's source text only. This is what the wrapper's sugar overload uses; it is
    /// the behavior of call sites that the generator does not intercept.
    /// </summary>
    public static AssertionHandle Degraded(Func<bool> assertion, string sourceText) => new(assertion, sourceText, null);

    /// <summary>Infrastructure for generated code. Not intended for direct use.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static AssertionHandle Generated(Action assertion) => new(null, null, assertion);
  }
}
