using System.ComponentModel;
using System.Threading;

namespace Assertive.Mocking
{
  /// <summary>A <see cref="Capture{T}"/> that can be cleared when its mock is reset.</summary>
  internal interface ICaptureSink
  {
    void Clear();
  }

  public static partial class Mock
  {
    private readonly struct MatcherEntry
    {
      public MatcherEntry(Func<object, bool> predicate, ICaptureSink? sink)
      {
        Predicate = predicate;
        Sink = sink;
      }

      public Func<object, bool> Predicate { get; }
      public ICaptureSink? Sink { get; }
    }

    private static readonly AsyncLocal<Queue<MatcherEntry>?> _predicates = new();

    // Sinks dequeued by the current interceptor, handed to CaptureMatchers so the owning mock can
    // clear them on Reset.
    private static readonly AsyncLocal<List<ICaptureSink>?> _dequeuedSinks = new();

    // Set by any matcher helper so a context that never gets an interceptor (e.g. InOrder, generic
    // methods) can detect that a matcher was written and fail loudly instead of silently comparing
    // default(T). Cleared only by ClearMatchers (not by ClearPendingMatchers).
    private static readonly AsyncLocal<bool> _matcherUsed = new();

    internal static bool MatcherWasUsed => _matcherUsed.Value;

    /// <summary>Discards any matchers that were not consumed by an interceptor (queue only).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ClearPendingMatchers()
    {
      _predicates.Value = null;
      _dequeuedSinks.Value = null;
    }

    /// <summary>
    /// Discards predicate matchers that were enqueued before the current intercepted call's own
    /// matchers. Called by a generated interceptor with the number of predicate arguments it owns,
    /// before it dequeues them.
    /// <para>
    /// A matcher helper whose call is never intercepted (e.g. the matcher is stored in a local and
    /// passed on a later statement) leaves its predicate in the queue. A call's own matchers are
    /// always the most recently enqueued, so dropping everything but the last
    /// <paramref name="keepLast"/> prevents the stale predicate from binding the wrong argument.
    /// </para>
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void PrunePendingMatchers(int keepLast)
    {
      if (_predicates.Value is not { Count: > 0 } queue)
      {
        return;
      }

      // Queue.Dequeue removes from the front, so drop the oldest until only the most recently
      // enqueued `keepLast` remain. The call's own matchers are dequeued in argument order after
      // this, and their relative order is preserved.
      while (queue.Count > keepLast)
      {
        queue.Dequeue();
      }
    }

    internal static void ClearMatchers()
    {
      ClearPendingMatchers();
      _matcherUsed.Value = false;
    }

    /// <summary>Consumes the captures dequeued for the interceptor that is currently being invoked.</summary>
    internal static List<ICaptureSink>? TakeDequeuedSinks()
    {
      var sinks = _dequeuedSinks.Value;
      _dequeuedSinks.Value = null;
      return sinks;
    }

    /// <summary>Matches any argument of type <typeparamref name="T"/>.</summary>
    public static T Any<T>()
    {
      _matcherUsed.Value = true;
      return default!;
    }

    /// <summary>Matches an argument of type <typeparamref name="T"/> satisfying <paramref name="predicate"/>.</summary>
    public static T Any<T>(Func<T, bool> predicate)
    {
      _matcherUsed.Value = true;
      (_predicates.Value ??= new Queue<MatcherEntry>()).Enqueue(new MatcherEntry(o => predicate((T)o), null));
      return default!;
    }

    /// <summary>
    /// Matches any argument of type <typeparamref name="T"/> and records the value into
    /// <paramref name="capture"/>. Inspect <see cref="Capture{T}.Latest"/> or
    /// <see cref="Capture{T}.Values"/> after the call to retrieve captured arguments.
    /// </summary>
    public static T Any<T>(Capture<T> capture)
    {
      _matcherUsed.Value = true;
      (_predicates.Value ??= new Queue<MatcherEntry>()).Enqueue(
        new MatcherEntry(o => { capture.Record((T)o!); return true; }, capture));
      return default!;
    }

    /// <summary>Matches when the argument is not null.</summary>
    public static T IsNotNull<T>() where T : class => Any<T>(x => x != null);

    /// <summary>Matches when the argument is one of the given values.</summary>
    public static T IsIn<T>(params T[] values) => Any<T>(x => values.Contains(x));

    /// <summary>Matches when the argument is in the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static T IsInRange<T>(T min, T max) where T : IComparable<T> =>
      Any<T>(x => x.CompareTo(min) >= 0 && x.CompareTo(max) <= 0);

    /// <summary>
    /// Matches an <c>IEnumerable&lt;T&gt;</c> argument that contains <paramref name="item"/>.
    /// The collection type is inferred from <paramref name="item"/>, so no type annotation is needed:
    /// <c>Contains(42L)</c> matches any <c>IEnumerable&lt;long&gt;</c> containing 42.
    /// </summary>
    public static IEnumerable<T> Contains<T>(T item) =>
      Any<IEnumerable<T>>(coll => coll != null && coll.Contains(item));

    /// <summary>
    /// Matches an <c>IEnumerable&lt;T&gt;</c> argument that has no elements.
    /// </summary>
    public static IEnumerable<T> IsEmpty<T>() =>
      Any<IEnumerable<T>>(coll => coll != null && !coll.Any());

    /// <summary>Dequeues the next predicate matcher (called by generated interceptors, in argument order).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Func<object, bool> DequeueMatcher()
    {
      if (_predicates.Value is not { Count: > 0 } queue)
      {
        throw new InvalidOperationException("Assertive.Mocking: no predicate matcher available — Any<T>(predicate) must appear directly inside the intercepted mock call.");
      }

      var entry = queue.Dequeue();
      if (entry.Sink is not null)
      {
        (_dequeuedSinks.Value ??= new List<ICaptureSink>()).Add(entry.Sink);
      }

      return entry.Predicate;
    }
  }
}
