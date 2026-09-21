using System;
using System.Threading;
using System.Threading.Tasks;
using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

// ---------------------------------------------------------------------------
// Regression tests for the independent review of PR #12.
// Each of these fails without the corresponding fix.
// ---------------------------------------------------------------------------

public interface IGenericEcho
{
  T Echo<T>(int value);
  string Greet(string name);
}

public sealed class RealGreeter : IGreeter
{
  public string Greet(string name) => "real:" + name;
  public void Log(string message) { }
  public string Complex(byte a, string b) => "real";
  public IGizmo GetGizmo(string id) => throw new NotSupportedException();
  public Task DoWorkAsync() => Task.CompletedTask;
  public Task<string> NameAsync() => Task.FromResult("real");
  public Task<IGizmo> GetGizmoAsync(string id) => throw new NotSupportedException();
}

/// <summary>B1 — capture state must be per async context, not a shared field on the mock.</summary>
public class CaptureThreadSafetyRegressionTests : MockingTestBase
{
  [Fact]
  public void Concurrent_call_cannot_overwrite_the_capture_of_an_in_flight_Received()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("a");
    greeter.Greet("a");

    using var mainHasCaptured = new ManualResetEventSlim(false);
    using var workerHasCalled = new ManualResetEventSlim(false);

    var worker = new Thread(() =>
    {
      mainHasCaptured.Wait();
      // A plain, non-capturing call. With an instance-field capture (the bug) this overwrites the
      // call that the main thread is in the middle of verifying.
      greeter.Log("worker");
      workerHasCalled.Set();
    });
    worker.Start();

    // The verification captures Greet("a") and then waits — deterministically — while another
    // thread calls the same mock before EndGlobalCapture reads the capture back.
    Received(() =>
    {
      greeter.Greet("a");
      mainHasCaptured.Set();
      workerHasCalled.Wait(TimeSpan.FromSeconds(5));
    }, Times.Exactly(2));

    worker.Join();
  }

  [Fact]
  public void Concurrent_received_verifications_on_the_same_mock_do_not_interfere()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("a");
    greeter.Log("b");

    var wrong = 0;
    var threads = new Thread[4];

    for (var t = 0; t < threads.Length; t++)
    {
      var verifyLog = t % 2 == 1;
      threads[t] = new Thread(() =>
      {
        for (var i = 0; i < 10_000; i++)
        {
          try
          {
            if (verifyLog) Received(() => greeter.Log("b"));
            else Received(() => greeter.Greet("a"));
          }
          catch { Interlocked.Increment(ref wrong); }
        }
      });
      threads[t].Start();
    }

    foreach (var thread in threads) thread.Join();

    Assert(() => wrong == 0);
  }
}

/// <summary>M2 — the matcher predicate queue must not leak when a call is not intercepted.</summary>
public class MatcherQueueLeakRegressionTests : MockingTestBase
{
  [Fact]
  public void Matcher_on_a_non_mock_receiver_does_not_leak_into_the_next_arrangement()
  {
    var real = new RealGreeter();

    var greeter = A<IGreeter>(g =>
    {
      // Non-mock receiver: the generated interceptor must drop the queued predicate.
      real.Greet(Any<string>(s => s == "x"));
      g.Greet(Any<string>(s => s == "y")).Returns("ok");
    });

    Assert(() => greeter.Greet("y") == "ok");
    Assert(() => greeter.Greet("x") == null);
  }

  [Fact]
  public void Matcher_on_a_non_interceptable_generic_call_does_not_leak()
  {
    var mock = A<IGenericEcho>();

    Setup(mock, m =>
    {
      // Generic methods are not intercepted; the stub must drop the queued predicate.
      Throws<NotSupportedException>(() => m.Echo<int>(Any<int>(x => x == 1)));
      m.Greet(Any<string>(s => s == "y")).Returns("ok");
    });

    Assert(() => mock.Greet("y") == "ok");
    Assert(() => mock.Greet("x") == null);
  }
}

/// <summary>M4 — strict mocks must be arrangeable with exact args in the standalone form.</summary>
public class StrictStandaloneArrangeRegressionTests : MockingTestBase
{
  [Fact]
  public void Strict_mock_can_be_arranged_standalone_with_exact_args()
  {
    var greeter = A<IGreeter>(MockMode.Strict);

    greeter.Greet("x").Returns("hi");

    Assert(() => greeter.Greet("x") == "hi");
  }

  [Fact]
  public void Strict_mock_still_throws_on_an_unarranged_call()
  {
    var greeter = A<IGreeter>(MockMode.Strict);

    greeter.Greet("x").Returns("hi");

    Throws<StrictMockException>(() => greeter.Greet("y"));
  }

  [Fact]
  public void Strict_mock_can_be_arranged_standalone_with_when_exact_args()
  {
    var greeter = A<IGreeter>(MockMode.Strict);

    When(() => greeter.Log("x")).Throws(new InvalidOperationException("boom"));

    Throws<InvalidOperationException>(() => greeter.Log("x"));
    Throws<StrictMockException>(() => greeter.Log("y"));
  }

  [Fact]
  public void Strict_mock_can_be_arranged_standalone_with_a_parameterless_method()
  {
    var greeter = A<IGreeter>(MockMode.Strict);

    greeter.NameAsync().Returns("hi");

    Assert(() => greeter.NameAsync().Result == "hi");
  }
}

/// <summary>M5 — InOrder must reject matchers instead of silently comparing default(T).</summary>
public class InOrderMatcherRegressionTests : MockingTestBase
{
  [Fact]
  public void InOrder_with_a_matcher_throws_a_clear_error()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("a");
    greeter.Greet("b");

    var ex = Throws<InvalidOperationException>(() => InOrder(greeter, g =>
    {
      g.Greet(Any<string>(s => s == "b"));
    }));

    Assert(() => ex.Message.Contains("not supported inside InOrder"));
  }
}

/// <summary>Minor review fixes.</summary>
public class ReviewMinorRegressionTests : MockingTestBase
{
  [Fact]
  public void Times_Between_with_max_below_min_throws()
  {
    Throws<ArgumentOutOfRangeException>(() => Times.Between(5, 2));
  }

  [Fact]
  public void Times_Between_with_negative_min_throws()
  {
    Throws<ArgumentOutOfRangeException>(() => Times.Between(-1, 2));
  }

  [Fact]
  public void ReturnsSequentially_with_no_values_throws_a_clear_error()
  {
    var greeter = A<IGreeter>();

    Throws<ArgumentException>(() => greeter.Greet("x").ReturnsSequentially());
  }

  [Fact]
  public void Reset_clears_capture_values()
  {
    var captured = new Capture<string>();
    var greeter = A<IGreeter>(g => g.Greet(Any(captured)).Returns("ok"));

    greeter.Greet("Alice");
    Assert(() => captured.Values.Count == 1);

    Reset(greeter);

    Assert(() => captured.Values.Count == 0);
  }
}
