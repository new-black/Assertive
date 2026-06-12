using System;
using System.ComponentModel;

namespace Assertive.Runtime
{
  /// <summary>
  /// The shape of a potentially-throwing sub-expression recorded by the source generator.
  /// Infrastructure for generated code; not intended to be used directly.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public enum ExceptionStepKind
  {
    /// <summary>An instance field/property access (`x.Member`).</summary>
    Member,
    /// <summary>`array.Length` (distinct wording in NullReferenceException messages).</summary>
    ArrayLength,
    /// <summary>An instance method call (`x.M(...)`).</summary>
    Call,
    /// <summary>A static or (reduced) extension method call.</summary>
    StaticCall,
    /// <summary>An element access (`x[i]`), covering arrays, indexers and dictionaries.</summary>
    Index,
    /// <summary>An explicit reference/unboxing cast (`(T)x`).</summary>
    Cast,
    /// <summary>A `/` expression.</summary>
    Divide,
    /// <summary>A `%` expression.</summary>
    Modulo,
    /// <summary>
    /// A call taking a lambda literal over a collection (`xs.Any(x =&gt; ...)`); carries the
    /// steps of the lambda body so the cause can be attributed to a specific item.
    /// </summary>
    LambdaIteration,
  }

  /// <summary>
  /// One potentially-throwing sub-expression of an intercepted assertion, recorded by the
  /// source generator in evaluation order. When the assertion delegate throws, the runtime
  /// walks these steps to attribute the exception to its cause (the equivalent of the
  /// expression-tree exception patterns). Evaluator delegates take the bound lambda item
  /// and index (ignored for top-level steps). Infrastructure for generated code; not
  /// intended to be used directly.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class ExceptionStep
  {
    public ExceptionStepKind Kind;

    /// <summary>Source text of the whole sub-expression (the cause, when this step matches).</summary>
    public string? NodeSource;

    /// <summary>The accessed member / called method name.</summary>
    public string? MemberName;

    /// <summary>Method calls: name + argument list as written (`Single(l =&gt; l &gt; 1)`).</summary>
    public string? MethodDisplay;

    public string? ReceiverSource;

    /// <summary>The final member name of the receiver expression, for "thrown inside" wording.</summary>
    public string? ReceiverLastMemberName;

    /// <summary>Evaluates the receiver (Member/Call/Index), cast operand (Cast), or extension-call source (StaticCall).</summary>
    public Func<object?, int, object?>? Receiver;

    /// <summary>Evaluates the whole sub-expression.</summary>
    public Func<object?, int, object?>? Node;

    public string? IndexSource;
    public bool IndexIsConstant;
    public Func<object?, int, object?>? Index;
    public bool IsArray;
    public bool IsDictionary;

    public string? LeftSource;
    public string? RightSource;
    public bool RightIsConstant;
    public Func<object?, int, object?>? Right;

    /// <summary>Cast: the target type.</summary>
    public Type? TargetType;

    /// <summary>StaticCall: display name of the declaring type (`int`, `Convert`).</summary>
    public string? StaticTypeName;
    public bool IsParsingMethod;
    public string? ParseTargetTypeName;

    /// <summary>First/FirstOrDefault/Single/SingleOrDefault (by name, like the old pattern).</summary>
    public bool IsLinqElementMethod;

    /// <summary>Whether the call has a predicate lambda argument.</summary>
    public bool Filtered;

    /// <summary>The predicate, applied to a candidate element (first arg) under the bound item/index.</summary>
    public Func<object?, object?, int, bool>? Filter;

    public string?[]? ArgSources;
    public bool[]? ArgIsConstant;
    public Func<object?, int, object?>?[]? Args;

    /// <summary>Index into Args of the first string-typed argument (parse input), or -1.</summary>
    public int StringArgIndex = -1;

    public string? CollectionSource;
    public Func<object?, int, object?>? Collection;
    public ExceptionStep[]? ItemSteps;
  }
}
