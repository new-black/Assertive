using System.ComponentModel;
using System.Runtime.CompilerServices;
using Assertive.Runtime;
using Assertive.Mocking;

namespace Assertive.Mocking.Runtime
{
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

  /// <summary>
  /// Bundles a return value and out/ref parameter values for a single arrangement.
  /// Produced by <see cref="ArrangeExtensions.ReturnsWithOuts{TResult}"/> and <see cref="WhenBuilder.SetsOuts"/>;
  /// consumed by generated mock members that have out or ref parameters.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class OutResult
  {
    public OutResult(object? returnValue, params object?[] outValues)
    {
      ReturnValue = returnValue;
      OutValues = outValues;
    }

    public object? ReturnValue { get; }
    public object?[] OutValues { get; }
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

      var isStandalone = _arrangingMock == null;
      if (isStandalone)
        _standaloneArrangeMock = null; // consume

      if (mock.CapturedMatch is { } spec)
      {
        mock.CapturedMatch = null;
        // Matcher-bearing calls go through the interceptor (not OnCall), so nothing is in _calls.
        return (mock, spec.Method, spec.Match);
      }

      if (mock.Captured is { } captured)
      {
        var args = captured.Arguments;
        // Non-matcher standalone arrange: OnCall recorded this call in _calls, remove it so arrange
        // calls don't appear in verification.
        if (isStandalone)
          mock.RemoveLastCall(captured.Method, args);
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
      lock (_lock)
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

    /// <summary>Removes the most recent call matching the given method and arguments from the call history.</summary>
    internal void RemoveLastCall(string method, object?[] args)
    {
      lock (_lock)
      {
        for (var i = _calls.Count - 1; i >= 0; i--)
        {
          if (_calls[i].Method == method && ArgumentsEqual(_calls[i].Arguments, args))
          {
            _calls.RemoveAt(i);
            return;
          }
        }
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
}
