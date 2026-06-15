using System.ComponentModel;
using System.Runtime.CompilerServices;
using Assertive.Runtime;

namespace Assertive.Mocking
{
  /// <summary>
  /// Describes how many times a mock member is expected to have been called.
  /// </summary>
  public readonly struct Times
  {
    private readonly int _min;
    private readonly int _max; // -1 = unbounded

    private Times(int min, int max) { _min = min; _max = max; }

    /// <summary>Exactly one call.</summary>
    public static Times Once => new Times(1, 1);
    /// <summary>Zero calls.</summary>
    public static Times Never => new Times(0, 0);
    /// <summary>One or more calls.</summary>
    public static Times AtLeastOnce => new Times(1, -1);
    /// <summary>Zero or one call.</summary>
    public static Times AtMostOnce => new Times(0, 1);
    /// <summary>Exactly <paramref name="n"/> calls.</summary>
    public static Times Exactly(int n) => new Times(n, n);
    /// <summary>At least <paramref name="n"/> calls.</summary>
    public static Times AtLeast(int n) => new Times(n, -1);
    /// <summary>At most <paramref name="n"/> calls.</summary>
    public static Times AtMost(int n) => new Times(0, n);
    /// <summary>Between <paramref name="min"/> and <paramref name="max"/> calls (inclusive).</summary>
    public static Times Between(int min, int max) => new Times(min, max);

    public bool Matches(int count) =>
      count >= _min && (_max == -1 || count <= _max);

    public string Describe() => (_min, _max) switch
    {
      (0, 0) => "never",
      (1, 1) => "exactly once",
      (0, 1) => "at most once",
      (1, -1) => "at least once",
      var (mn, mx) when mn == mx => $"exactly {mn} time(s)",
      (var mn, -1) => $"at least {mn} time(s)",
      (0, var mx) => $"at most {mx} time(s)",
      var (mn, mx) => $"between {mn} and {mx} time(s)",
    };
  }

  /// <summary>
  /// Collects argument values captured during matched mock calls. Use <see cref="Mock.Any{T}(Capture{T})"/>
  /// inside an arrange or verify lambda to record the values passed to a mock member.
  /// </summary>
  public sealed class Capture<T>
  {
    private readonly List<T> _values = new();
    /// <summary>All values captured in order of recording.</summary>
    public IReadOnlyList<T> Values => _values;
    /// <summary>The most recently captured value. Throws if nothing has been captured yet.</summary>
    public T Latest => _values.Count > 0 ? _values[_values.Count - 1] : throw new InvalidOperationException("No captured values.");
    internal void Record(T value) => _values.Add(value);
  }


  /// <summary>
  /// Marker a <c>.Throws(...)</c> arrangement yields instead of throwing directly. The generated
  /// member decides how to surface it: a synchronous <c>throw</c> for sync methods, a faulted
  /// <c>Task</c>/<c>ValueTask</c> for async ones — so awaiting a mocked async method observes the
  /// exception, matching real async semantics.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class MockFault
  {
    public MockFault(Exception exception) => Exception = exception;

    public Exception Exception { get; }
  }

  /// <summary>One recorded (or captured) call against a mock: the method name and argument values.</summary>
  public sealed record MockInvocation(string Method, object?[] Arguments)
  {
    public string Format()
    {
      if (Method.StartsWith("get_", StringComparison.Ordinal))
      {
        var prop = Method.Substring(4);
        return Arguments.Length == 0
          ? prop
          : $"[{string.Join(", ", Arguments.Select(FormatArg))}]";
      }
      if (Method.StartsWith("set_", StringComparison.Ordinal))
      {
        var prop = Method.Substring(4);
        return Arguments.Length == 1
          ? $"{prop} = {FormatArg(Arguments[0])}"
          : $"[{string.Join(", ", Arguments.Take(Arguments.Length - 1).Select(FormatArg))}] = {FormatArg(Arguments[Arguments.Length - 1])}";
      }
      return $"{Method}({string.Join(", ", Arguments.Select(FormatArg))})";
    }

    private static string FormatArg(object? arg) => GeneratedAssert.SerializeValue(arg);
  }

  /// <summary>
  /// Implemented by every generated mock so the recording engine can be reached uniformly.
  /// Interface mocks inherit <see cref="MockBase"/> (which returns itself); class mocks can't
  /// (single inheritance — they extend the mocked class), so they hold a <see cref="MockBase"/>
  /// and return it here.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public interface IMockObject
  {
    MockBase Core { get; }
  }

  /// <summary>
  /// The recording engine for a mock: recorded calls, configured setups, and the "capture" mode
  /// used by arrange/Received. Interface mocks derive it; class mocks
  /// compose it (held as a field) since they must extend the mocked class instead.
  ///
  /// Entirely reflection-free, so generated mocks are Native-AOT and trimming safe — unlike
  /// runtime proxy generators (Castle DynamicProxy) which current mocking libraries rely on.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public class MockBase : IMockObject
  {
    private readonly string _typeName;

    public MockBase(string typeName) => _typeName = typeName;

    MockBase IMockObject.Core => this;

    private readonly object _lock = new object();

    private readonly List<MockInvocation> _calls = new();

    // A setup is (method name, argument matcher, behavior). The matcher decides whether a call
    // matches — exact-args setups use value equality, method-group setups (Any) use `_ => true`.
    // A behavior is a function of the call arguments that yields the return value, throws, or
    // runs a side effect — so void (Throws/Does) and value (Returns/Throws/Does) share one path.
    private readonly List<(string Method, Func<object?[], bool> Match, Func<object?[], object?> Behavior)> _setups = new();

    /// <summary>When true, member calls record the invocation as <see cref="Captured"/> instead of running.</summary>
    internal bool Capturing;
    internal MockInvocation? Captured;

    /// <summary>When true, a call that matches no arrangement throws instead of returning a default/auto-mock.</summary>
    internal bool Strict;

    /// <summary>
    /// Set by a generated matcher interceptor instead of <see cref="Captured"/>'s exact args: the
    /// method name plus the argument-match predicate built from the call's matchers. Consumed (and
    /// cleared) by the arrange verbs, <c>When</c>, and <c>Received</c>.
    /// </summary>
    internal (string Method, Func<object?[], bool> Match)? CapturedMatch;

    /// <summary>When non-null, all captures during a <see cref="CaptureSequence"/> call are appended here.</summary>
    [ThreadStatic]
    private static List<MockInvocation>? _sequenceCapture;

    /// <summary>
    /// Set by <see cref="BeginGlobalCapture"/> so that the next mock call on any mock (whether it
    /// goes through the interceptor or through <see cref="OnCall"/> directly) records itself as the
    /// global capture result. Used by <c>MockDSL.Received(() =&gt; mock.Method(...))</c>.
    /// </summary>
    [ThreadStatic]
    private static bool _globalCapturing;

    [ThreadStatic]
    private static MockBase? _globalCapturingResult;

    internal static void BeginGlobalCapture()
    {
      _globalCapturing = true;
      _globalCapturingResult = null;
    }

    internal static void AbortGlobalCapture()
    {
      _globalCapturing = false;
      _globalCapturingResult = null;
    }

    internal static (MockBase Mock, MockInvocation Call, (string Method, Func<object?[], bool> Match)? MatchSpec) EndGlobalCapture()
    {
      _globalCapturing = false;
      var mock = _globalCapturingResult
        ?? throw new InvalidOperationException(
          "Assertive.Mocking: Received() / DidNotReceive() lambda did not invoke a mock method. " +
          "Pass a lambda that calls a single mock member, e.g. Received(() => mock.Method(args)).");
      _globalCapturingResult = null;
      var call = mock.Captured!;
      var match = mock.CapturedMatch;
      mock.CapturedMatch = null;
      return (mock, call, match);
    }

    /// <summary>
    /// Captures a call described by per-argument matchers (generated interceptor entry point).
    /// <paramref name="displayArguments"/> are the placeholder values, used only for messages.
    /// </summary>
    public void CaptureMatchers(string method, Func<object, bool>[] matchers, object?[] displayArguments)
    {
      Captured = new MockInvocation(method, displayArguments);
      CapturedMatch = (method, args =>
      {
        if (args.Length != matchers.Length)
        {
          return false;
        }

        for (var i = 0; i < args.Length; i++)
        {
          if (!matchers[i](args[i]!))
          {
            return false;
          }
        }

        return true;
      });

      // Matcher-bearing calls go through the interceptor and never reach OnCall, so we must
      // record the global capture result here as well.
      if (_globalCapturing)
      {
        _globalCapturingResult = this;
        _globalCapturing = false;
      }

      // Support the standalone arrange form: var mock = A<T>(); mock.Method(default).Returns(v);
      // Matcher interceptors never reach OnCall, so record the pending target here.
      if (!Capturing)
        _standaloneArrangeMock = this;
    }

    /// <summary>
    /// The mock whose arrange lambda is currently executing on this thread, so that a trailing
    /// <c>.Returns(value)</c> knows which mock + captured call to attach to (the NSubstitute trick).
    /// </summary>
    [ThreadStatic]
    private static MockBase? _arrangingMock;

    /// <summary>
    /// Tracks the last mock that had a method called on it (via interceptor or OnCall), enabling
    /// the standalone arrange form: <c>var m = A&lt;T&gt;(); m.Method(args).Returns(v);</c>.
    /// Cleared after an arrange verb consumes it.
    /// </summary>
    [ThreadStatic]
    private static MockBase? _standaloneArrangeMock;

    internal void BeginArrange()
    {
      Capturing = true;
      Captured = null;
      _arrangingMock = this;
    }

    internal void EndArrange()
    {
      Capturing = false;
      _arrangingMock = null;
    }

    /// <summary>
    /// Resolves the mock and arg-match function for the most recent arrange call, consuming any
    /// pending standalone target. Prefers the explicit lambda scope (<see cref="_arrangingMock"/>)
    /// over the standalone thread-static (<see cref="_standaloneArrangeMock"/>).
    /// </summary>
    private static (MockBase Mock, string Method, Func<object?[], bool> Match) ResolveArrangeTarget()
    {
      var mock = _arrangingMock ?? _standaloneArrangeMock
        ?? throw new InvalidOperationException(
          "Assertive.Mocking: an arrange verb (Returns/Throws/Does) was called with no preceding mock call on this thread.");

      if (_arrangingMock == null)
        _standaloneArrangeMock = null; // consume

      if (mock.CapturedMatch is { } spec)
      {
        mock.CapturedMatch = null;
        return (mock, spec.Method, spec.Match);
      }

      if (mock.Captured is { } captured)
      {
        var args = captured.Arguments;
        return (mock, captured.Method, a => ArgumentsEqual(args, a));
      }

      throw new InvalidOperationException("Assertive.Mocking: an arrange verb has no preceding mock call to attach to.");
    }

    /// <summary>
    /// Attaches a conditional behavior to the last call captured during the active arrange scope.
    /// The setup only fires when <paramref name="condition"/> returns true at call time; otherwise
    /// the engine falls through to the next matching setup (or the default/auto-mock value).
    /// </summary>
    public static void AttachConditionalBehaviorToLastArrangedCall(Func<object?[], object?> behavior, Func<bool> condition)
    {
      var (mock, method, argMatch) = ResolveArrangeTarget();
      mock.AddSetup(method, args => argMatch(args) && condition(), behavior);
    }

    /// <summary>
    /// Attaches a behavior to the last call captured during the active arrange scope. Used by
    /// the fluent <c>.Returns</c>/<c>.Throws</c>/<c>.Does</c> verbs, which run immediately after
    /// the mock call they configure.
    /// </summary>
    public static void AttachBehaviorToLastArrangedCall(Func<object?[], object?> behavior)
    {
      var (mock, method, argMatch) = ResolveArrangeTarget();
      mock.AddSetup(method, argMatch, behavior);
    }

    /// <summary>The mock arranging on this thread, its captured call, and its argument matcher — used by <c>When(...)</c>.</summary>
    internal static (MockBase Mock, MockInvocation Call, Func<object?[], bool> Match) CurrentCapture()
    {
      var mock = _arrangingMock
        ?? throw new InvalidOperationException("Assertive.Mocking: When(...) was called outside of an A<T>(...) arrange lambda.");

      if (mock.Captured is not { } call)
      {
        throw new InvalidOperationException("Assertive.Mocking: When(() => ...) did not capture a mock call.");
      }

      // Prefer a matcher (from a matcher interceptor); fall back to exact-argument equality.
      var match = mock.CapturedMatch is { } spec
        ? spec.Match
        : new Func<object?[], bool>(args => ArgumentsEqual(call.Arguments, args));

      mock.CapturedMatch = null;

      return (mock, call, match);
    }

    /// <summary>
    /// Called from generated event <c>add</c> accessors. When in capture mode, records the event
    /// name in <see cref="Captured"/> and returns <c>true</c> so the accessor returns immediately
    /// without subscribing. Returns <c>false</c> during normal execution.
    /// </summary>
    public bool TryCaptureEvent(string eventName)
    {
      if (!Capturing)
      {
        return false;
      }

      Captured = new MockInvocation(eventName, Array.Empty<object>());
      CapturedMatch = null;
      return true;
    }

    /// <summary>The calls this mock actually received, in order.</summary>
    public IReadOnlyList<MockInvocation> Calls => _calls;

    /// <summary>
    /// Adds a behavior matching a specific method + arguments (value equality). Used by arrange
    /// verbs and <c>When(...)</c>.
    /// </summary>
    public void AddBehavior(MockInvocation expected, Func<object?[], object?> behavior)
    {
      lock (_lock)
      {
        _setups.Add((expected.Method, args => ArgumentsEqual(expected.Arguments, args), behavior));
      }
    }

    /// <summary>
    /// Adds a behavior matching a method by name with a custom argument matcher. Used by the
    /// method-group form <c>Any(mock.Method)</c> (matcher <c>_ =&gt; true</c>) and predicate matchers.
    /// </summary>
    public void AddSetup(string method, Func<object?[], bool> match, Func<object?[], object?> behavior)
    {
      lock (_lock)
      {
        _setups.Add((method, match, behavior));
      }
    }

    /// <summary>
    /// Called at the top of every generated member. In capture mode it records the expected
    /// call and signals the generated member to return immediately; otherwise it records the
    /// real call and yields the configured return value (if a matching setup exists).
    /// </summary>
    public bool OnCall(string method, object?[] arguments, out object? configuredReturn, out bool matched)
    {
      if (Capturing)
      {
        Captured = new MockInvocation(method, arguments);
        CapturedMatch = null;   // an exact (non-matcher) capture
        _sequenceCapture?.Add(Captured);
        configuredReturn = null;
        matched = false;
        return true;
      }

      if (_globalCapturing)
      {
        Captured = new MockInvocation(method, arguments);
        CapturedMatch = null;
        _globalCapturingResult = this;
        _globalCapturing = false;
        configuredReturn = null;
        matched = false;
        return true;
      }

      // Standalone arrange: record this call so a subsequent Returns/Throws/Does can attach to it.
      Captured = new MockInvocation(method, arguments);
      CapturedMatch = null;
      _standaloneArrangeMock = this;

      lock (_lock)
      {
        _calls.Add(new MockInvocation(method, arguments));

        // Last-registered match wins, so a later, more specific arrangement overrides an earlier
        // broad one (e.g. Any(...) first, then an exact-args override).
        for (var i = _setups.Count - 1; i >= 0; i--)
        {
          var (setupMethod, match, behavior) = _setups[i];

          if (setupMethod == method && match(arguments))
          {
            // The behavior may return a value, throw, or run a side effect. For void methods the
            // generated member ignores the return; a throwing behavior still propagates.
            configuredReturn = behavior(arguments);
            matched = true;
            return false;
          }
        }

        // Strict: nothing implicit. An unarranged call is an error (no default, no auto-mock).
        if (Strict)
        {
          var arranged = _setups.Count == 0
            ? "(no arrangements)"
            : string.Join(", ", _setups.Select(s => s.Method).Distinct());

          throw new StrictMockException(
            $"Strict mock of {InterfaceName()}: {new MockInvocation(method, arguments).Format()} was called, " +
            $"but no matching arrangement was set up.{Environment.NewLine}Arranged: {arranged}");
        }

        configuredReturn = null;
        matched = false;
        return false;
      }
    }

    private string InterfaceName() => _typeName;

    private readonly List<(MockInvocation Call, object Mock)> _autoMocks = new();

    /// <summary>
    /// Returns a memoized child mock for an unarranged interface-returning call, so repeated calls
    /// (with equal arguments) yield the same instance — you can arrange/verify on it, and reference
    /// identity holds. Each call site/argument combination gets its own child (recursive auto-mock).
    /// </summary>
    public object GetOrCreateAutoMock(string method, object?[] arguments, Func<object> factory)
    {
      foreach (var (call, mock) in _autoMocks)
      {
        if (call.Method == method && ArgumentsEqual(call.Arguments, arguments))
        {
          return mock;
        }
      }

      var created = factory();
      _autoMocks.Add((new MockInvocation(method, arguments), created));
      return created;
    }

    private static bool ArgumentsEqual(object?[] a, object?[] b)
    {
      if (a.Length != b.Length)
      {
        return false;
      }

      for (var i = 0; i < a.Length; i++)
      {
        if (!ValueEqual(a[i], b[i]))
        {
          return false;
        }
      }

      return true;
    }

    private static bool ValueEqual(object? x, object? y)
    {
      // Fast path: handles null-null, same-reference, and types that override Equals (records, strings, etc.)
      if (Equals(x, y)) return true;

      // Structural collection equality — arrays, List<T>, etc. don't override Equals.
      // The generic IEnumerable<object> path covers most strongly-typed collections.
      if (x is System.Collections.Generic.IEnumerable<object?> ex &&
          y is System.Collections.Generic.IEnumerable<object?> ey)
      {
        return ex.SequenceEqual(ey, StructuralEqualityComparer.Instance);
      }

      // Non-generic IEnumerable fallback (e.g. int[], ArrayList, IEnumerable<int> coerced).
      if (x is System.Collections.IEnumerable xe && y is System.Collections.IEnumerable ye)
      {
        return xe.Cast<object?>().SequenceEqual(ye.Cast<object?>(), StructuralEqualityComparer.Instance);
      }

      return false;
    }

    private sealed class StructuralEqualityComparer : System.Collections.Generic.IEqualityComparer<object?>
    {
      public static readonly StructuralEqualityComparer Instance = new();
      public new bool Equals(object? x, object? y) => ValueEqual(x, y);
      public int GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;
    }

    private readonly Dictionary<string, Delegate?> _eventHandlers = new();

    /// <summary>Subscribes <paramref name="handler"/> to the named event (called from generated event add accessors).</summary>
    public void AddEventHandler(string eventName, Delegate? handler)
    {
      lock (_lock)
      {
        _eventHandlers.TryGetValue(eventName, out var existing);
        _eventHandlers[eventName] = Delegate.Combine(existing, handler);
      }
    }

    /// <summary>Unsubscribes <paramref name="handler"/> from the named event (called from generated event remove accessors).</summary>
    public void RemoveEventHandler(string eventName, Delegate? handler)
    {
      lock (_lock)
      {
        _eventHandlers.TryGetValue(eventName, out var existing);
        _eventHandlers[eventName] = Delegate.Remove(existing, handler);
      }
    }

    /// <summary>Returns the combined delegate for the named event, or null if no subscribers are registered.</summary>
    public Delegate? GetEventHandler(string eventName)
    {
      lock (_lock)
      {
        _eventHandlers.TryGetValue(eventName, out var handler);
        return handler;
      }
    }

    /// <summary>Clears the recorded call log (use before <see cref="Mock.VerifyNoOtherCalls{T}"/>).</summary>
    public void ClearCalls()
    {
      lock (_lock)
      {
        _calls.Clear();
      }
    }

    /// <summary>Clears both the recorded call log and all configured setups, returning the mock to a pristine state.</summary>
    public void Reset()
    {
      lock (_lock)
      {
        _calls.Clear();
        _setups.Clear();
      }
    }

    /// <summary>
    /// Runs <paramref name="execute"/> in capture mode, collecting every captured invocation into
    /// a list (rather than only keeping the last one, as <see cref="BeginArrange"/> does). Used by
    /// <see cref="Mock.InOrder{T}"/>.
    /// </summary>
    public List<MockInvocation> CaptureSequence(Action execute)
    {
      var captured = new List<MockInvocation>();
      Capturing = true;
      _sequenceCapture = captured;
      try { execute(); }
      finally { Capturing = false; _sequenceCapture = null; }
      return captured;
    }

    /// <summary>Public wrapper over the private <c>ArgumentsEqual</c> helper, for use by <see cref="Mock.InOrder{T}"/>.</summary>
    public bool ArgumentsMatchEqual(object?[] a, object?[] b) => ArgumentsEqual(a, b);

    /// <summary>Returns true when at least one setup matches <paramref name="call"/> (used by VerifyNoOtherCalls).</summary>
    public bool HasMatchingSetup(MockInvocation call)
    {
      lock (_lock)
      {
        foreach (var (method, match, _) in _setups)
        {
          if (method == call.Method && match(call.Arguments))
          {
            return true;
          }
        }

        return false;
      }
    }
  }

  /// <summary>How a mock responds to calls that match no arrangement.</summary>
  public enum MockMode
  {
    /// <summary>Unarranged calls return a default / recursively auto-mocked value.</summary>
    Loose,

    /// <summary>Unarranged calls throw <see cref="StrictMockException"/>.</summary>
    Strict,
  }

  /// <summary>Thrown when a strict mock receives a call that matches no arrangement.</summary>
  public sealed class StrictMockException : Exception
  {
    public StrictMockException(string message) : base(message) { }
  }

  /// <summary>
  /// Entry points for source-generated mocking. The implementation type is source-generated and
  /// registered into <see cref="MockFactoryRegistry"/> by a module initializer, so creation is
  /// reflection-free and AOT-safe.
  /// </summary>
  public static partial class Mock
  {
    /// <summary>The recording engine behind a generated mock (interface or class).</summary>
    internal static MockBase CoreOf(object mock)
      => mock is IMockObject m
        ? m.Core
        : throw new InvalidOperationException($"Assertive.Mocking: '{mock.GetType().Name}' is not a generated mock.");

    /// <summary>
    /// Verifies the mock received the call expressed by <paramref name="call"/>. On failure it
    /// throws an Assertive failure built through <see cref="GeneratedAssert"/>, so the message
    /// reuses Assertive's value rendering, string diffing and layout.
    /// </summary>
    public static void Received<T>(T mock, Action<T> call, [CallerArgumentExpression(nameof(call))] string callExpression = "") where T : class
    {
      var @base = AsBase(mock);

      @base.Capturing = true;

      try
      {
        call(mock);
      }
      finally
      {
        @base.Capturing = false;
      }

      var expected = @base.Captured!;

      // Matcher interceptor → use its argument matcher; otherwise exact-argument equality.
      var matchSpec = @base.CapturedMatch;
      @base.CapturedMatch = null;
      var matches = matchSpec is { } spec
        ? new Func<MockInvocation, bool>(c => c.Method == spec.Method && spec.Match(c.Arguments))
        : c => c.Method == expected.Method && c.Arguments.SequenceEqual(expected.Arguments);

      // A matching call satisfies the verification.
      if (@base.Calls.Any(matches))
      {
        return;
      }

      var assertionText = StripLambdaParameter(callExpression);
      var received = @base.Calls.Count == 0
        ? "(no calls received)"
        : string.Join("\n", @base.Calls.Select((c, i) => $"  [{i}] {c.Format()}"));

      var sameMethod = @base.Calls.Where(c => c.Method == expected.Method).ToList();

      if (sameMethod.Count > 0)
      {
        // Method was called but with different arguments.
        throw GeneratedAssert.Failure(
          $"{assertionText}\n\nExpected: {expected.Format()}\nReceived:\n{received}",
          System.Array.Empty<(string, object?)>(),
          message: null,
          context: null,
          contextExpression: null);
      }

      // The method was never called at all.
      throw GeneratedAssert.Failure(
        $"{assertionText}\n\nExpected the mock to have received {expected.Format()}, but it never was.\n\nReceived calls:\n{received}",
        System.Array.Empty<(string, object?)>(),
        message: null,
        context: null,
        contextExpression: null);
    }

    /// <summary>
    /// Verifies the mock received the call expressed by <paramref name="call"/> exactly the
    /// number of times described by <paramref name="times"/>. Throws an Assertive failure on mismatch.
    /// </summary>
    public static void Received<T>(T mock, Times times, Action<T> call,
      [CallerArgumentExpression(nameof(call))] string callExpression = "") where T : class
    {
      var @base = AsBase(mock);

      @base.Capturing = true;

      try
      {
        call(mock);
      }
      finally
      {
        @base.Capturing = false;
      }

      var expected = @base.Captured!;

      var matchSpec = @base.CapturedMatch;
      @base.CapturedMatch = null;
      var matches = matchSpec is { } spec
        ? new Func<MockInvocation, bool>(c => c.Method == spec.Method && spec.Match(c.Arguments))
        : c => c.Method == expected.Method && c.Arguments.SequenceEqual(expected.Arguments);

      var count = @base.Calls.Count(matches);

      if (times.Matches(count))
      {
        return;
      }

      var received = @base.Calls.Count == 0
        ? "(no calls received)"
        : string.Join("\n", @base.Calls.Select((c, i) => $"  [{i}] {c.Format()}"));

      throw GeneratedAssert.Failure(
        $"Expected {expected.Format()} to be received {times.Describe()}, but was received {count} time(s).\n\nReceived calls:\n{received}",
        System.Array.Empty<(string, object?)>(),
        message: null,
        context: null,
        contextExpression: null);
    }

    /// <summary>
    /// Verifies the mock never received the call expressed by <paramref name="call"/>.
    /// Equivalent to <c>Received(mock, Times.Never, call)</c>.
    /// </summary>
    public static void DidNotReceive<T>(T mock, Action<T> call,
      [CallerArgumentExpression(nameof(call))] string callExpression = "") where T : class
      => Received(mock, Times.Never, call, callExpression);

    /// <summary>
    /// Verifies that the calls expressed in <paramref name="sequence"/> were received in that
    /// relative order. Other calls may be interleaved; only relative ordering is checked.
    /// </summary>
    public static void InOrder<T>(T mock, Action<T> sequence,
      [CallerArgumentExpression(nameof(sequence))] string sequenceExpression = "") where T : class
    {
      var core = AsBase(mock);
      var expected = core.CaptureSequence(() => sequence(mock));
      var actual = core.Calls.ToList();

      // Walk actual calls finding each expected call in order.
      var pos = 0;
      foreach (var exp in expected)
      {
        while (pos < actual.Count && !(actual[pos].Method == exp.Method && core.ArgumentsMatchEqual(exp.Arguments, actual[pos].Arguments)))
          pos++;

        if (pos >= actual.Count)
          throw GeneratedAssert.Failure(
            $"InOrder: expected {exp.Format()} but it was not found after position {pos} in the received call log.\n\nReceived calls:\n{FormatCalls(actual)}",
            locals: System.Array.Empty<(string, object?)>(),
            message: null, context: null, contextExpression: null);

        pos++;
      }
    }

    private static string FormatCalls(IEnumerable<MockInvocation> calls) =>
      string.Join("\n", calls.Select((c, i) => $"  [{i}] {c.Format()}"));

    /// <summary>
    /// Verifies that every call recorded on <paramref name="mock"/> matches at least one
    /// configured setup. Any call that has no matching setup is considered "unexpected" and
    /// causes a failure. Call your <c>Received</c> assertions first, then call this
    /// to assert nothing else happened.
    /// </summary>
    public static void VerifyNoOtherCalls<T>(T mock) where T : class
    {
      var @base = AsBase(mock);
      var unmatched = @base.Calls.Where(c => !@base.HasMatchingSetup(c)).ToList();

      if (unmatched.Count == 0)
      {
        return;
      }

      throw GeneratedAssert.Failure(
        $"Mock received unexpected calls:\n{string.Join("\n", unmatched.Select((c, i) => $"  [{i}] {c.Format()}"))}",
        locals: System.Array.Empty<(string, object?)>(),
        message: null,
        context: null,
        contextExpression: null);
    }

    /// <summary>Clears all recorded calls on <paramref name="mock"/>.</summary>
    public static void ClearReceivedCalls<T>(T mock) where T : class => AsBase(mock).ClearCalls();

    /// <summary>Clears both the recorded call log and all configured setups on <paramref name="mock"/>.</summary>
    public static void Reset<T>(T mock) where T : class => AsBase(mock).Reset();

    /// <summary>
    /// Raises an event on the mock. <paramref name="attach"/> is used to capture the event name —
    /// execute it in capture mode to record which event was subscribed. Then invokes all
    /// current subscribers with <paramref name="args"/>.
    /// </summary>
    public static void Raise<T>(T mock, Action<T> attach, params object?[] args) where T : class
    {
      var core = AsBase(mock);

      // Capture the event name by executing attach in capturing mode.
      core.Capturing = true;
      try { attach(mock); }
      finally { core.Capturing = false; }

      var eventName = core.Captured?.Method
        ?? throw new InvalidOperationException("Assertive.Mocking: Mock.Raise could not capture the event name.");

      var handler = core.GetEventHandler(eventName);
      handler?.DynamicInvoke(args);
    }

    private static MockBase AsBase<T>(T mock) where T : class => CoreOf(mock!);

    private static string StripLambdaParameter(string expression)
    {
      // Turn "g => g.Greet(\"Bob\")" into "Greet(\"Bob\")" for a clean assertion header.
      var arrow = expression.IndexOf("=>", StringComparison.Ordinal);

      if (arrow < 0)
      {
        return expression;
      }

      var body = expression.Substring(arrow + 2).Trim();
      var dot = body.IndexOf('.');

      return dot >= 0 ? body.Substring(dot + 1) : body;
    }
  }
}
