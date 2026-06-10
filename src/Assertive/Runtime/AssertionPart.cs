using System;
using System.ComponentModel;

namespace Assertive.Runtime
{
  /// <summary>
  /// How an assertion part combines its children — mirrors the C# operator at that
  /// position in the assertion body. Infrastructure for generated code.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public enum AssertionPartKind
  {
    Leaf,
    /// <summary>`&amp;&amp;`: the right side is only evaluated (and reported) when the left side passes.</summary>
    AndAlso,
    /// <summary>`&amp;`: both sides are evaluated; every failing leaf is reported.</summary>
    And,
    /// <summary>`||`: when the whole assertion failed, both sides failed and both are reported.</summary>
    OrElse,
    /// <summary>`|`: both sides are evaluated; every failing leaf is reported.</summary>
    Or,
  }

  /// <summary>
  /// A node of a logically-composed assertion (`a &amp;&amp; b`, `a &amp; b`, ...), recorded by the
  /// source generator so each conjunct reports as its own assertion — `&amp;&amp;` is the
  /// documented way to put multiple asserts in one statement. The generated equivalent of
  /// AssertionTreeProvider/AssertionTreeExecutor. Infrastructure for generated code; not
  /// intended to be used directly.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class AssertionPart
  {
    public AssertionPartKind Kind;

    public AssertionPart? Left;
    public AssertionPart? Right;

    /// <summary>Leaf: re-evaluates the leaf (pasted source, exact semantics).</summary>
    public Func<bool>? Condition;

    /// <summary>Leaf: builds the failure report for this leaf (classified decomposition, custom pattern, or source text).</summary>
    public Func<Exception>? Failure;

    /// <summary>Leaf: builds the report when re-evaluating the leaf throws (with the leaf's exception steps).</summary>
    public Func<Exception, Exception>? ExceptionFailure;

    /// <summary>Leaf: source text, for last-resort reporting.</summary>
    public string? Source;
  }
}
