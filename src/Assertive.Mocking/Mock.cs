using System.Runtime.CompilerServices;
using Assertive.Runtime;

namespace Assertive.Mocking
{
  public static partial class Mock
  {
    /// <summary>Creates a mock of <typeparamref name="T"/> with no arrangements.</summary>
    public static T A<T>(MockMode mode = MockMode.Loose) where T : class
    {
      var mock = MockFactoryRegistry.Create<T>(System.Array.Empty<object>());
      Mock.CoreOf(mock).Strict = mode == MockMode.Strict;
      return mock;
    }

    /// <summary>
    /// Creates a mock of class <typeparamref name="T"/> with no arrangements, forwarding
    /// <paramref name="constructorArguments"/> to a matching base constructor.
    /// </summary>
    public static T A<T>(MockMode mode, params object?[] constructorArguments) where T : class
    {
      var mock = MockFactoryRegistry.Create<T>(constructorArguments);
      Mock.CoreOf(mock).Strict = mode == MockMode.Strict;
      return mock;
    }

    /// <summary>
    /// Creates a loose mock of class <typeparamref name="T"/> with no arrangements, forwarding
    /// <paramref name="constructorArguments"/> to a matching base constructor.
    /// Use <c>A&lt;T&gt;(MockMode.Loose, args)</c> to also set the mode.
    /// </summary>
    public static T A<T>(params object?[] constructorArguments) where T : class
      => MockFactoryRegistry.Create<T>(constructorArguments);

    /// <summary>
    /// Creates a mock of <typeparamref name="T"/> and runs <paramref name="arrange"/> against it
    /// to configure it. The lambda is <b>executed</b> (matching how Assertive executes assertion
    /// delegates): ordinary code in it runs normally, and each <c>mock.Member(args).Returns(value)</c>
    /// records a configured return. The source generator's only job here is the recording
    /// implementation of <typeparamref name="T"/> — arrangement is pure runtime, so arbitrary
    /// setup code (locals, loops, I/O, conditionals) behaves exactly as written.
    /// </summary>
    public static T A<T>(System.Action<T> arrange, MockMode mode = MockMode.Loose) where T : class
    {
      var mock = A<T>(mode);
      var @base = Mock.CoreOf(mock);

      @base.BeginArrange();

      try
      {
        arrange(mock);
      }
      finally
      {
        @base.EndArrange();
      }

      return mock;
    }

    /// <summary>
    /// Creates a spy wrapper around <paramref name="wrapped"/>: all calls delegate to the real
    /// implementation by default, but can be set up with <see cref="Setup{T}"/> and verified
    /// just like a regular mock. Only non-generic interface types are supported; the source
    /// generator emits a typed wrapper class.
    /// </summary>
    public static T Wrap<T>(T wrapped) where T : class
      => WrapFactoryRegistry.Create(wrapped);

    /// <summary>
    /// Configures calls on an already-created mock or spy. Use this when the instance was created
    /// outside of an <c>A&lt;T&gt;(setup)</c> lambda — for example after <see cref="Wrap{T}"/>.
    /// </summary>
    public static void Setup<T>(T mock, System.Action<T> setup) where T : class
    {
      var @base = Mock.CoreOf(mock);
      @base.BeginArrange();
      try { setup(mock); }
      finally { @base.EndArrange(); }
    }

    /// <summary>
    /// Constructs <typeparamref name="T"/> using its richest accessible constructor.
    /// Each provided argument is matched to a constructor parameter by type (not by position);
    /// unmatched parameters are auto-mocked. The source generator emits a typed factory per
    /// call site — no reflection at runtime, fully AOT-safe.
    /// </summary>
    public static T Build<T>() where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>() was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0, object? a1) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0, a1) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0, object? a1, object? a2) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0, a1, a2) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0, object? a1, object? a2, object? a3) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0..a3) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0, object? a1, object? a2, object? a3, object? a4) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0..a4) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0, object? a1, object? a2, object? a3, object? a4, object? a5) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0..a5) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <inheritdoc cref="Build{T}()"/>
    public static T Build<T>(object? a0, object? a1, object? a2, object? a3, object? a4, object? a5, object? a6) where T : class
      => throw new InvalidOperationException("Assertive.Mocking: Build<T>(a0..a6) was not intercepted. Ensure Assertive.Mocking.Generators is referenced by this project.");

    /// <summary>Verifies that the mock call expressed by <paramref name="call"/> was received at least once.</summary>
    public static void Received(System.Action call, [CallerArgumentExpression(nameof(call))] string callExpression = "")
      => ReceivedCore(call, Times.AtLeastOnce, callExpression);

    /// <summary>Verifies that the mock call expressed by <paramref name="call"/> was received exactly as many times as <paramref name="times"/> describes.</summary>
    public static void Received(System.Action call, Times times, [CallerArgumentExpression(nameof(call))] string callExpression = "")
      => ReceivedCore(call, times, callExpression);

    /// <summary>Verifies that the mock call expressed by <paramref name="call"/> was never received.</summary>
    public static void DidNotReceive(System.Action call, [CallerArgumentExpression(nameof(call))] string callExpression = "")
      => ReceivedCore(call, Times.Never, callExpression);

    private static void ReceivedCore(System.Action call, Times times, string callExpression)
    {
      MockBase.BeginGlobalCapture();
      try { call(); }
      catch { MockBase.AbortGlobalCapture(); throw; }

      var (mock, captured, matchSpec) = MockBase.EndGlobalCapture();

      var matches = matchSpec is { } spec
        ? new System.Func<MockInvocation, bool>(c => c.Method == spec.Method && spec.Match(c.Arguments))
        : c => c.Method == captured.Method && System.Linq.Enumerable.SequenceEqual(c.Arguments, captured.Arguments);

      var count = System.Linq.Enumerable.Count(mock.Calls, matches);

      if (times.Matches(count)) return;

      var assertionText = StripArrow(callExpression);
      var received = mock.Calls.Count == 0
        ? "(no calls received)"
        : string.Join("\n", System.Linq.Enumerable.Select(mock.Calls, (c, i) => $"  [{i}] {c.Format()}"));

      throw GeneratedAssert.Failure(
        $"Expected {captured.Format()} to be received {times.Describe()}, but was received {count} time(s).\n\nReceived calls:\n{received}",
        System.Array.Empty<(string, object?)>(),
        message: null, context: null, contextExpression: null);
    }

    private static string StripArrow(string expression)
    {
      var arrow = expression.IndexOf("=>", System.StringComparison.Ordinal);
      if (arrow < 0) return expression;
      var body = expression.Substring(arrow + 2).Trim();
      var dot = body.IndexOf('.');
      return dot >= 0 ? body.Substring(dot + 1) : body;
    }

    /// <summary>
    /// Arranges a call expressed as a delegate, for cases the fluent suffix can't express —
    /// chiefly <b>void methods</b> (a void expression can't be the receiver of <c>.Returns</c>),
    /// but also value methods you want to throw or run a callback. The delegate runs once to
    /// capture the call; the returned builder configures its behavior.
    /// </summary>
    public static WhenBuilder When(System.Action call)
    {
      call();
      var (mock, captured, match) = MockBase.CurrentCapture();
      return new WhenBuilder(mock, captured.Method, match);
    }
  }

  /// <summary>Configures the behavior of the call captured by <see cref="Mock.When"/>.</summary>
  public readonly struct WhenBuilder
  {
    private readonly MockBase _mock;
    private readonly string _method;
    private readonly Func<object?[], bool> _match;

    internal WhenBuilder(MockBase mock, string method, Func<object?[], bool> match)
    {
      _mock = mock;
      _method = method;
      _match = match;
    }

    /// <summary>Makes the call fault with <paramref name="exception"/> (synchronously, or as a faulted task for async methods).</summary>
    public void Throws(System.Exception exception) => _mock.AddSetup(_method, _match, _ => new MockFault(exception));

    /// <summary>Makes the call fault with a new <typeparamref name="TException"/>.</summary>
    public void Throws<TException>() where TException : System.Exception, new()
      => _mock.AddSetup(_method, _match, _ => new MockFault(new TException()));

    /// <summary>Runs <paramref name="callback"/> (with the call arguments) when the call happens.</summary>
    public void Does(System.Action<object?[]> callback)
      => _mock.AddSetup(_method, _match, args =>
      {
        callback(args);
        return null;
      });
  }

  /// <summary>Fluent arrangement verbs for value-returning members, used as <c>mock.Member(args).Verb(...)</c>.</summary>
  public static class ArrangeExtensions
  {
    /// <summary>Configures the preceding mock call to return <paramref name="value"/>.</summary>
    public static void Returns<TResult>(this TResult call, TResult value)
    {
      _ = call;
      MockBase.AttachBehaviorToLastArrangedCall(_ => value);
    }

    /// <summary>
    /// Configures a <c>Task&lt;T&gt;</c>-returning call to complete with <paramref name="value"/> —
    /// the bare result, no <c>Task.FromResult</c> and no <c>ReturnsAsync</c> suffix. More specific
    /// than the <c>TResult</c> overload, so <c>.Returns(value)</c> binds here.
    /// </summary>
    public static void Returns<T>(this System.Threading.Tasks.Task<T> call, T value)
    {
      _ = call;
      MockBase.AttachBehaviorToLastArrangedCall(_ => System.Threading.Tasks.Task.FromResult(value));
    }

    /// <summary>Configures a <c>ValueTask&lt;T&gt;</c>-returning call to complete with <paramref name="value"/>.</summary>
    public static void Returns<T>(this System.Threading.Tasks.ValueTask<T> call, T value)
    {
      _ = call;
      MockBase.AttachBehaviorToLastArrangedCall(_ => new System.Threading.Tasks.ValueTask<T>(value));
    }

    /// <summary>Configures the preceding mock call to fault with <paramref name="exception"/> (sync throw, or faulted task for async).</summary>
    public static void Throws<TResult>(this TResult call, System.Exception exception)
    {
      _ = call;
      MockBase.AttachBehaviorToLastArrangedCall(_ => new MockFault(exception));
    }

    /// <summary>Configures the preceding mock call to run <paramref name="callback"/> and return its result.</summary>
    public static void Does<TResult>(this TResult call, System.Func<object?[], TResult> callback)
    {
      _ = call;
      MockBase.AttachBehaviorToLastArrangedCall(args => callback(args));
    }

    /// <summary>
    /// Arranges sequential return values on the preceding mock call: each call consumes the next
    /// value in sequence; after exhaustion the last value is returned repeatedly.
    /// </summary>
    public static void ReturnsMany<TResult>(this TResult call, params TResult[] values)
    {
      _ = call;
      var idx = 0;
      MockBase.AttachBehaviorToLastArrangedCall(_ => values[System.Math.Min(idx++, values.Length - 1)]);
    }

    /// <summary>
    /// Configures the preceding mock call to return <paramref name="value"/> only when
    /// <paramref name="when"/> returns true at call time. When the condition is false the
    /// setup is skipped and the next matching setup (or default) is used instead.
    /// </summary>
    public static void Returns<TResult>(this TResult call, TResult value, System.Func<bool> when)
    {
      _ = call;
      var condition = when;
      MockBase.AttachConditionalBehaviorToLastArrangedCall(_ => value, condition);
    }
  }
}
