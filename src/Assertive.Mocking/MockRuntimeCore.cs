using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
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

    /// <summary>
    /// True while this mock is being arranged (any arrange scope for it is active) or captured via
    /// <see cref="CaptureSequence"/>/<c>Mock.Raise</c>. When true, member calls record the invocation
    /// as the pending capture instead of running.
    /// </summary>
    internal bool Capturing =>
      ReferenceEquals(_capturingMock.Value, this)
      || (_arrangeScopes.Value is { Count: > 0 } scopes && scopes.Contains(this));

    /// <summary>When true, a call that matches no arrangement throws instead of returning a default/auto-mock.</summary>
    internal bool Strict;

    /// <summary>
    /// True while <see cref="Mock.When"/><c>(() =&gt; ...)</c> is executing its probe call. A probe
    /// must be captured rather than treated as an unarranged call, so the strict check is skipped
    /// for it (otherwise a strict mock could not be arranged with the void <c>When</c> form).
    /// </summary>
    private static readonly AsyncLocal<bool> _arrangeProbe = new();

    internal static bool BeginArrangeProbe()
    {
      var previous = _arrangeProbe.Value;
      _arrangeProbe.Value = true;
      return previous;
    }

    internal static void EndArrangeProbe(bool previous) => _arrangeProbe.Value = previous;

    /// <summary>
    /// A pending capture in the current async context: the last call made on a mock (or a matcher
    /// description of it) that a subsequent arrange verb / <c>When</c> / <c>Received</c> consumes.
    /// Stored per mock in <see cref="_pendingCaptures"/> rather than on the mock itself so that
    /// concurrent threads (or interleaved async flows) touching the same mock cannot clobber each
    /// other's capture.
    /// </summary>
    private sealed class PendingCapture
    {
      public MockInvocation? Call;
      public (string Method, Func<object?[], bool> Match)? Match;
    }

    // Copy-on-write: assigning a fresh dictionary isolates this async context, while inherited
    // values (e.g. a Task that captured this ExecutionContext) can never mutate ours.
    private static readonly AsyncLocal<Dictionary<MockBase, PendingCapture>?> _pendingCaptures = new();

    private PendingCapture Pending =>
      _pendingCaptures.Value is { } captures && captures.TryGetValue(this, out var pending)
        ? pending
        : new PendingCapture();

    /// <summary>The pending call captured for this mock in the current async context, if any.</summary>
    internal MockInvocation? CapturedCall => Pending.Call;

    private void SetCapture(MockInvocation call, (string Method, Func<object?[], bool> Match)? match)
    {
      var current = _pendingCaptures.Value;
      var next = current is null
        ? new Dictionary<MockBase, PendingCapture>()
        : new Dictionary<MockBase, PendingCapture>(current);
      next[this] = new PendingCapture { Call = call, Match = match };
      _pendingCaptures.Value = next;
    }

    private void ClearCapture()
    {
      if (_pendingCaptures.Value is not { } current || !current.ContainsKey(this))
      {
        return;
      }

      var next = new Dictionary<MockBase, PendingCapture>(current);
      next.Remove(this);
      _pendingCaptures.Value = next;
    }

    /// <summary>The mock whose arrange lambda is currently executing (innermost scope), if any.</summary>
    private static MockBase? CurrentArrangeMock =>
      _arrangeScopes.Value is { Count: > 0 } scopes ? scopes[scopes.Count - 1] : null;

    /// <summary>When non-null, all captures during a <see cref="CaptureSequence"/> call are appended here.</summary>
    private static readonly AsyncLocal<List<MockInvocation>?> _sequenceCapture = new();

    /// <summary>
    /// Set by <see cref="BeginGlobalCapture"/> so that the next mock call on any mock (whether it
    /// goes through the interceptor or through <see cref="OnCall"/> directly) records itself as the
    /// global capture result. Used by <c>Mock.Received(() =&gt; mock.Method(...))</c>.
    /// </summary>
    private static readonly AsyncLocal<bool> _globalCapturing = new();

    private static readonly AsyncLocal<MockBase?> _globalCapturingResult = new();

    internal static void BeginGlobalCapture()
    {
      Mock.ClearMatchers();
      _globalCapturing.Value = true;
      _globalCapturingResult.Value = null;
    }

    internal static void AbortGlobalCapture()
    {
      _globalCapturing.Value = false;
      _globalCapturingResult.Value = null;
      Mock.ClearMatchers();
    }

    internal static (MockBase Mock, MockInvocation Call, (string Method, Func<object?[], bool> Match)? MatchSpec) EndGlobalCapture()
    {
      _globalCapturing.Value = false;
      var mock = _globalCapturingResult.Value
        ?? throw new InvalidOperationException(
          "Assertive.Mocking: Received() / DidNotReceive() lambda did not invoke a mock method. " +
          "Pass a lambda that calls a single mock member, e.g. Received(() => mock.Method(args)).");
      _globalCapturingResult.Value = null;
      var pending = mock.Pending;
      var call = pending.Call!;
      var match = pending.Match;
      mock.ClearCapture();
      Mock.ClearMatchers();
      return (mock, call, match);
    }

    /// <summary>
    /// Captures a call described by per-argument matchers (generated interceptor entry point).
    /// <paramref name="parameterTypes"/> disambiguate overloads (a matcher like <c>Any&lt;int&gt;()</c>
    /// must not match an overload taking another type). <paramref name="displayArguments"/> are the
    /// placeholder values, used only for messages.
    /// </summary>
    public void CaptureMatchers(string method, Func<object, bool>[] matchers, Type[] parameterTypes, object?[] displayArguments)
    {
      if (Mock.TakeDequeuedSinks() is { } sinks)
      {
        lock (_lock)
        {
          _captures.AddRange(sinks);
        }
      }

      SetCapture(new MockInvocation(method, displayArguments), (method, args =>
      {
        if (args.Length != matchers.Length || !ParameterTypesMatch(parameterTypes, args))
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
      }));

      // Matcher-bearing calls go through the interceptor and never reach OnCall, so we must
      // record the global capture result here as well.
      if (_globalCapturing.Value)
      {
        if (_globalCapturingResult.Value is not null)
        {
          throw new InvalidOperationException(
            "Assertive.Mocking: Received() / DidNotReceive() lambdas must invoke a single mock method.");
        }

        _globalCapturingResult.Value = this;
      }

      // Support the standalone arrange form: var mock = A<T>(); mock.Method(default).Returns(v);
      // Matcher interceptors never reach OnCall, so record the pending target here.
      if (!Capturing)
        _standaloneArrangeMock.Value = this;
    }

    /// <summary>
    /// Nested arrange scopes in the current async context, innermost last. A stack (rather than a
    /// single value) so an <c>A&lt;T&gt;(...)</c> inside another arrange lambda doesn't clobber the
    /// outer scope.
    /// </summary>
    private static readonly AsyncLocal<List<MockBase>?> _arrangeScopes = new();

    /// <summary>Single-mock capture mode used by <see cref="CaptureSequence"/> and <c>Mock.Raise</c>.</summary>
    private static readonly AsyncLocal<MockBase?> _capturingMock = new();

    /// <summary>
    /// Tracks the last mock that had a method called on it (via interceptor or OnCall), enabling
    /// the standalone arrange form: <c>var m = A&lt;T&gt;(); m.Method(args).Returns(v);</c>.
    /// Cleared after an arrange verb consumes it.
    /// </summary>
    private static readonly AsyncLocal<MockBase?> _standaloneArrangeMock = new();

    internal void BeginArrange()
    {
      Mock.ClearMatchers();
      ClearCapture();

      var scopes = _arrangeScopes.Value;
      var next = scopes is null ? new List<MockBase>() : new List<MockBase>(scopes);
      next.Add(this);
      _arrangeScopes.Value = next;
    }

    internal void EndArrange()
    {
      var scopes = _arrangeScopes.Value;
      if (scopes is { Count: > 0 })
      {
        var next = new List<MockBase>(scopes);
        if (ReferenceEquals(next[next.Count - 1], this))
          next.RemoveAt(next.Count - 1);
        else
          next.Remove(this);
        _arrangeScopes.Value = next.Count == 0 ? null : next;
      }

      Mock.ClearMatchers();
    }

    /// <summary>Enters single-mock capture mode, returning the previous value to restore on exit.</summary>
    internal MockBase? BeginSingleCapture()
    {
      var previous = _capturingMock.Value;
      _capturingMock.Value = this;
      return previous;
    }

    /// <summary>Restores the capture mode saved by <see cref="BeginSingleCapture"/>.</summary>
    internal static void EndSingleCapture(MockBase? previous) => _capturingMock.Value = previous;

    /// <summary>
    /// Resolves the mock and arg-match function for the most recent arrange call, consuming any
    /// pending standalone target. Prefers the explicit lambda scope over the standalone async-local.
    /// </summary>
    private static (MockBase Mock, string Method, Func<object?[], bool> Match) ResolveArrangeTarget()
    {
      var scopeMock = CurrentArrangeMock;
      var mock = scopeMock ?? _standaloneArrangeMock.Value
        ?? throw new InvalidOperationException(
          "Assertive.Mocking: an arrange verb (Returns/Throws/Does) was called with no preceding mock call on this thread.");

      var isStandalone = scopeMock is null;
      if (isStandalone)
        _standaloneArrangeMock.Value = null; // consume

      var pending = mock.Pending;

      if (pending.Match is { } spec)
      {
        pending.Match = null;
        // Matcher-bearing calls go through the interceptor (not OnCall), so nothing is in _calls.
        return (mock, spec.Method, spec.Match);
      }

      if (pending.Call is { } captured)
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

    /// <summary>The mock arranging in this async context, its captured call, and its argument matcher — used by <c>When(...)</c>.</summary>
    internal static (MockBase Mock, MockInvocation Call, Func<object?[], bool> Match) CurrentCapture()
    {
      var scopeMock = CurrentArrangeMock;
      var mock = scopeMock ?? _standaloneArrangeMock.Value
        ?? throw new InvalidOperationException(
          "Assertive.Mocking: When(...) was called with no preceding mock call or active A<T>(...) arrange lambda.");

      var isStandalone = scopeMock is null;
      if (isStandalone)
        _standaloneArrangeMock.Value = null; // consume

      var pending = mock.Pending;
      if (pending.Call is not { } call)
      {
        throw new InvalidOperationException("Assertive.Mocking: When(() => ...) did not capture a mock call.");
      }

      // Prefer a matcher (from a matcher interceptor); fall back to exact-argument equality.
      var spec = pending.Match;
      var hasMatcher = spec is not null;
      var match = spec is { } matcherSpec
        ? matcherSpec.Match
        : new Func<object?[], bool>(args => ArgumentsEqual(call.Arguments, args));

      pending.Match = null;

      // Non-matcher standalone When recorded the probe call through OnCall; remove it so the
      // arrangement itself doesn't appear as a received call.
      if (isStandalone && !hasMatcher)
        mock.RemoveLastCall(call.Method, call.Arguments);

      return (mock, call, match);
    }

    /// <summary>
    /// Called from generated event <c>add</c> accessors. When in capture mode, records the event
    /// name in the pending capture and returns <c>true</c> so the accessor returns immediately
    /// without subscribing. Returns <c>false</c> during normal execution.
    /// </summary>
    public bool TryCaptureEvent(string eventName)
    {
      if (!Capturing)
      {
        return false;
      }

      SetCapture(new MockInvocation(eventName, Array.Empty<object>()), null);
      return true;
    }

    /// <summary>The calls this mock actually received, in order (a snapshot safe to enumerate while calls are recorded).</summary>
    public IReadOnlyList<MockInvocation> Calls
    {
      get
      {
        lock (_lock)
        {
          return _calls.ToArray();
        }
      }
    }

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
    /// When <paramref name="parameterTypes"/> is supplied, the call's argument types must match too,
    /// so arrangements on overloaded methods don't cross-match.
    /// </summary>
    public void AddSetup(string method, Func<object?[], bool> match, Func<object?[], object?> behavior, Type[]? parameterTypes = null)
    {
      var effectiveMatch = parameterTypes is null
        ? match
        : args => ParameterTypesMatch(parameterTypes, args) && match(args);

      lock (_lock)
      {
        _setups.Add((method, effectiveMatch, behavior));
      }
    }

    /// <summary>
    /// True when every argument is assignable to the corresponding declared parameter type. A
    /// boxed <c>Nullable&lt;T&gt;</c> arrives as a boxed <c>T</c>, so the underlying type is used.
    /// </summary>
    private static bool ParameterTypesMatch(Type[] parameterTypes, object?[] arguments)
    {
      if (parameterTypes.Length != arguments.Length)
      {
        return false;
      }

      for (var i = 0; i < parameterTypes.Length; i++)
      {
        var argument = arguments[i];
        if (argument is null)
        {
          continue;
        }

        var expected = Nullable.GetUnderlyingType(parameterTypes[i]) ?? parameterTypes[i];
        if (!expected.IsInstanceOfType(argument))
        {
          return false;
        }
      }

      return true;
    }

    /// <summary>
    /// Called at the top of every generated member. In capture mode it records the expected
    /// call and signals the generated member to return immediately; otherwise it records the
    /// real call and yields the configured return value (if a matching setup exists).
    /// </summary>
    public bool OnCall(string method, object?[] arguments, out object? configuredReturn, out bool matched)
    {
      // A call that reaches OnCall was not intercepted, so any matcher queued for it can never be
      // consumed. Drop it so it cannot bind the next predicate arrangement to the wrong argument.
      Mock.ClearPendingMatchers();

      if (Capturing)
      {
        var captured = new MockInvocation(method, arguments);
        SetCapture(captured, null);   // an exact (non-matcher) capture
        _sequenceCapture.Value?.Add(captured);
        configuredReturn = null;
        matched = false;
        return true;
      }

      if (_globalCapturing.Value)
      {
        if (_globalCapturingResult.Value is not null)
        {
          throw new InvalidOperationException(
            "Assertive.Mocking: Received() / DidNotReceive() lambdas must invoke a single mock method.");
        }

        SetCapture(new MockInvocation(method, arguments), null);
        _globalCapturingResult.Value = this;
        configuredReturn = null;
        matched = false;
        return true;
      }

      // Standalone arrange: record this call so a subsequent Returns/Throws/Does can attach to it.
      SetCapture(new MockInvocation(method, arguments), null);
      _standaloneArrangeMock.Value = this;

      lock (_lock)
      {
        // Last-registered match wins, so a later, more specific arrangement overrides an earlier
        // broad one (e.g. Any(...) first, then an exact-args override).
        for (var i = _setups.Count - 1; i >= 0; i--)
        {
          var (setupMethod, match, behavior) = _setups[i];

          if (setupMethod == method && match(arguments))
          {
            _calls.Add(new MockInvocation(method, arguments));
            // The behavior may return a value, throw, or run a side effect. For void methods the
            // generated member ignores the return; a throwing behavior still propagates.
            configuredReturn = behavior(arguments);
            matched = true;
            return false;
          }
        }

        // Strict: nothing implicit. An unarranged call is an error (no default, no auto-mock).
        // Checked before recording so a violation doesn't pollute the call log. A probe call made
        // by When(() => ...) is an arrangement in progress, not usage, so it is exempt.
        if (Strict && !_arrangeProbe.Value)
        {
          var arranged = _setups.Count == 0
            ? "(no arrangements)"
            : string.Join(", ", _setups.Select(s => s.Method).Distinct());

          throw new StrictMockException(
            $"Strict mock of {InterfaceName()}: {new MockInvocation(method, arguments).Format()} was called, " +
            $"but no matching arrangement was set up.{Environment.NewLine}Arranged: {arranged}");
        }

        _calls.Add(new MockInvocation(method, arguments));
        configuredReturn = null;
        matched = false;
        return false;
      }
    }

    private string InterfaceName() => _typeName;

    private readonly List<(MockInvocation Call, object Mock)> _autoMocks = new();

    /// <summary>Captures whose matchers were used with this mock, cleared by <see cref="Reset"/>.</summary>
    private readonly List<ICaptureSink> _captures = new();

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
        _autoMocks.Clear();
        foreach (var capture in _captures)
        {
          capture.Clear();
        }
        _captures.Clear();
      }

      ClearCapture();
    }

    /// <summary>
    /// Runs <paramref name="execute"/> in capture mode, collecting every captured invocation into
    /// a list (rather than only keeping the last one, as <see cref="BeginArrange"/> does). Used by
    /// <see cref="Mock.InOrder{T}"/>.
    /// </summary>
    public List<MockInvocation> CaptureSequence(Action execute)
    {
      var captured = new List<MockInvocation>();
      Mock.ClearMatchers();
      var previousCapturing = BeginSingleCapture();
      _sequenceCapture.Value = captured;
      var usedMatchers = false;
      try
      {
        execute();
      }
      finally
      {
        EndSingleCapture(previousCapturing);
        _sequenceCapture.Value = null;
        // InOrder calls are not intercepted, so a matcher used here can never take effect — it would
        // silently compare against default(T). Detect it and fail loudly instead.
        usedMatchers = Mock.MatcherWasUsed;
        Mock.ClearMatchers();
      }

      if (usedMatchers)
      {
        throw new InvalidOperationException(
          "Assertive.Mocking: argument matchers (Any<T>/IsIn/...) are not supported inside InOrder; " +
          "use exact argument values.");
      }

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
