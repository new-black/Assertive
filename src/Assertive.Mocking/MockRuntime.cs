using System.ComponentModel;
using System.Runtime.CompilerServices;
using Assertive.Runtime;
using Assertive.Mocking.Runtime;

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
