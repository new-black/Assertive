using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Sdk;

namespace Assertive.Test
{
  public class StackTraceFilterTests : AssertionTestBase
  {
    private static readonly string[] _filteredPatterns =
    [
      "System.Linq.Expressions.Interpreter.",
      "System.Dynamic.Utils.",
      "Assertive.AssertImpl.",
      "Assertive.Assert."
    ];

    [Fact]
    public void Stack_trace_filters_out_internal_frames()
    {
      
      XunitException? caught = null;
      try
      {
        var list = new List<int>();

        Assert.That(() => list.Single() == 1);
      }
      catch (XunitException ex)
      {
        caught = ex;
      }

      var message = StripAnsi(caught!.Message);

      Assert.That(() => message.Contains("STACKTRACE")
                        && _filteredPatterns.All(p => !message.Contains(p))
                        && message.Contains("System.Linq.ThrowHelper"));
    }

    // The runtime stack trace of the thrown failure (what the test framework reports)
    // starts at the user's code: all of Assertive's own frames carry [StackTraceHidden].

    [Fact]
    public void Runtime_stack_trace_hides_assertive_frames_for_intercepted_assertions()
    {
      var x = 1;

      var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => x == 2));

      AssertNoAssertiveFrames(ex.StackTrace);
      Xunit.Assert.Contains(nameof(Runtime_stack_trace_hides_assertive_frames_for_intercepted_assertions), ex.StackTrace);
    }

    [Fact]
    public void Runtime_stack_trace_hides_assertive_frames_for_degraded_assertions()
    {
      // A stored delegate is not intercepted: this fails through the plain ThatCore path.
      Func<bool> storedCondition = () => false;

      var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(storedCondition));

      AssertNoAssertiveFrames(ex.StackTrace);
      Xunit.Assert.Contains(nameof(Runtime_stack_trace_hides_assertive_frames_for_degraded_assertions), ex.StackTrace);
    }

    [Fact]
    public void Runtime_stack_trace_hides_assertive_frames_for_throws_assertions()
    {
      var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.Throws(() => { }));

      AssertNoAssertiveFrames(ex.StackTrace);
      Xunit.Assert.Contains(nameof(Runtime_stack_trace_hides_assertive_frames_for_throws_assertions), ex.StackTrace);
    }

    [Fact]
    public async System.Threading.Tasks.Task Async_throws_keeps_only_the_logical_throws_frame()
    {
      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert.Throws(async () => await System.Threading.Tasks.Task.CompletedTask));

      // Known limitation: async state machines don't inherit [StackTraceHidden], so the
      // logical Assert.Throws frame remains. Everything below it is still hidden.
      Xunit.Assert.Contains("at Assertive.Assert.Throws", ex.StackTrace);
      Xunit.Assert.DoesNotContain("at Assertive.AssertImpl", ex.StackTrace);
      Xunit.Assert.DoesNotContain("at Assertive.Runtime.", ex.StackTrace);
    }

    private static void AssertNoAssertiveFrames(string? stackTrace)
    {
      Xunit.Assert.NotNull(stackTrace);
      Xunit.Assert.DoesNotContain("at Assertive.Assert", stackTrace);
      Xunit.Assert.DoesNotContain("at Assertive.DSL", stackTrace);
      Xunit.Assert.DoesNotContain("at Assertive.Runtime.", stackTrace);
      Xunit.Assert.DoesNotContain("at Assertive.Generated", stackTrace);
    }
  }
}
