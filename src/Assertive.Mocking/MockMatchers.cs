using System.ComponentModel;

namespace Assertive.Mocking
{
  public static partial class Mock
  {
    [ThreadStatic]
    private static Queue<Func<object, bool>>? _predicates;

    /// <summary>Matches any argument of type <typeparamref name="T"/>.</summary>
    public static T Any<T>() => default!;

    /// <summary>Matches an argument of type <typeparamref name="T"/> satisfying <paramref name="predicate"/>.</summary>
    public static T Any<T>(Func<T, bool> predicate)
    {
      (_predicates ??= new Queue<Func<object, bool>>()).Enqueue(o => predicate((T)o));
      return default!;
    }

    /// <summary>
    /// Matches any argument of type <typeparamref name="T"/> and records the value into
    /// <paramref name="capture"/>. Inspect <see cref="Capture{T}.Latest"/> or
    /// <see cref="Capture{T}.Values"/> after the call to retrieve captured arguments.
    /// </summary>
    public static T Any<T>(Capture<T> capture)
    {
      (_predicates ??= new Queue<Func<object, bool>>()).Enqueue(o => { capture.Record((T)o!); return true; });
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
      if (_predicates is not { Count: > 0 } queue)
      {
        throw new InvalidOperationException("Assertive.Mocking: no predicate matcher available — Any<T>(predicate) must appear directly inside the intercepted mock call.");
      }

      return queue.Dequeue();
    }
  }
}
