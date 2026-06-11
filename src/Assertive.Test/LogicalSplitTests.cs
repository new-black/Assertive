using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
using Assertive.Plugin;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// End-to-end tests for logically-composed assertions through the public API:
  /// `&amp;&amp;` is the documented way to combine multiple asserts in a single statement, so
  /// every conjunct must report as its own assertion with its own pattern message.
  /// `&amp;&amp;`-only bodies re-evaluate with short-circuit semantics (one failing conjunct);
  /// bodies mixing in &amp;, | or || evaluate every leaf and report all failing ones.
  /// </summary>
  public class LogicalSplitTests : AssertionTestBase
  {
    [Fact]
    public void AndAlso_reports_only_the_first_failing_conjunct()
    {
      var a = 5;
      var b = 2;

      ShouldFail(() => a == 1 && b == 2);
    }

    [Fact]
    public void AndAlso_reports_the_second_conjunct_when_the_first_passes()
    {
      var a = 1;
      var b = 7;

      ShouldFail(() => a == 1 && b == 2);
    }

    [Fact]
    public void AndAlso_short_circuits_reporting_when_both_fail()
    {
      var a = 5;
      var b = 7;

      // && short-circuits: only the first failing conjunct is evaluated and reported.
      ShouldFail(() => a == 1 && b == 2);
    }

    [Fact]
    public void Bitwise_and_reports_every_failing_conjunct()
    {
      var a = 5;
      var b = 7;

      ShouldFail(() => (a == 1) & (b == 2));
    }

    [Fact]
    public void Bitwise_and_reports_only_the_failing_conjunct_when_the_other_passes()
    {
      var a = 1;
      var b = 7;

      ShouldFail(() => (a == 1) & (b == 2));
    }

    [Fact]
    public void OrElse_reports_both_sides_when_the_assertion_fails()
    {
      var a = 5;
      var b = 7;

      // If `a == 1 || b == 2` failed, both sides failed.
      ShouldFail(() => a == 1 || b == 2);
    }

    [Fact]
    public void Bitwise_or_reports_both_sides_when_the_assertion_fails()
    {
      var a = 5;
      var b = 7;

      ShouldFail(() => (a == 1) | (b == 2));
    }

    [Fact]
    public void Chained_AndAlso_reports_the_first_failing_conjunct_only()
    {
      var a = 1;
      var b = 7;
      var c = 9;

      ShouldFail(() => a == 1 && b == 2 && c == 3);
    }

    [Fact]
    public void Parenthesized_groups_report_every_failing_leaf()
    {
      var a = 5;
      var b = 7;
      var c = 9;

      // Bodies that mix in || (or &, |) evaluate every leaf and report all failing ones;
      // the operator structure itself is not replayed.
      ShouldFail(() => (a == 1 && b == 2) || c == 3);
    }

    [Fact]
    public void Each_conjunct_reports_with_its_own_pattern()
    {
      var s = "hello";
      var n = 3;

      ShouldFail(() => s.StartsWith("x") & n > 5);
    }

    [Fact]
    public void Negated_conjunct_reports_with_negated_pattern()
    {
      var list = new List<int> { 1, 2, 3 };
      var count = 3;

      ShouldFail(() => !list.Contains(2) && list.Count == count);
    }

    [Fact]
    public void Conjuncts_keep_their_full_pattern_repertoire()
    {
      string? name = "set";
      var flag = false;

      // Null pattern leaf passes, bool leaf fails.
      ShouldFail(() => name != null && flag);
    }

    [Fact]
    public void Combined_message_contains_a_block_per_failing_conjunct()
    {
      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var a = 5;
        var b = 7;

        ShouldFail(() => (a == 1) & (b == 2));
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void Single_failing_conjunct_message_is_headed_by_that_conjunct()
    {
      var a = 5;
      var b = 2;

      ShouldFail(() => a == 1 && b == 2);
    }

    [Fact]
    public void Locals_of_the_failing_conjunct_are_reported()
    {
      var items = new[] { 1, 2 };
      var flag = true;

      ShouldFail(() => items.Length == 5 && flag);
    }

    [Fact]
    public void Throwing_conjunct_reports_the_exception_cause()
    {
      List<int>? list = null;
      var x = 1;

      ShouldFail(() => list!.Count == 0 && x == 1);
    }

    [Fact]
    public void Conjuncts_over_private_types_are_split_reflectively()
    {
      var counter = new Counter();
      var a = 5;

      // Both leaves report: the opaque call leaf by source text, the equality decomposed.
      ShouldFail(() => counter.Run("one", false) & a == 1);

      Xunit.Assert.Equal(2, counter.ExecutionCount("one"));
    }

    [Fact]
    public void AndAlso_does_not_reevaluate_short_circuited_conjuncts()
    {
      var counter = new Counter();

      try
      {
        Assert(() => counter.Run("one", false) && counter.Run("two", true));
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch
      {
        // Original evaluation + failure-path re-evaluation; "two" never runs.
        Xunit.Assert.Equal(2, counter.ExecutionCount("one"));
        Xunit.Assert.Equal(0, counter.ExecutionCount("two"));
      }
    }

    [Fact]
    public void AndAlso_reevaluates_up_to_the_failing_conjunct()
    {
      var counter = new Counter();

      try
      {
        Assert(() => counter.Run("one", true) && counter.Run("two", false));
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch
      {
        Xunit.Assert.Equal(2, counter.ExecutionCount("one"));
        Xunit.Assert.Equal(2, counter.ExecutionCount("two"));
      }
    }

    [Fact]
    public void Bitwise_and_evaluates_all_conjuncts()
    {
      var counter = new Counter();

      var ex = Xunit.Assert.ThrowsAny<Exception>(() =>
        Assert(() => counter.Run("one", false) & counter.Run("two", false)));

      Xunit.Assert.Equal(2, counter.ExecutionCount("one"));
      Xunit.Assert.Equal(2, counter.ExecutionCount("two"));

      SnapshotMessage(ex);
    }

    [Fact]
    public void Passing_composed_assertion_does_not_throw()
    {
      var a = 1;
      var b = 2;

      Assert(() => a == 1 && b == 2);
      Assert(() => (a == 1) & (b == 2));
      Assert(() => a == 5 || b == 2);
      Assert(() => (a == 5) | (b == 2));
    }

    private class Counter
    {
      private readonly Dictionary<string, int> _executions = new();

      public int ExecutionCount(string name)
      {
        _executions.TryGetValue(name, out var count);
        return count;
      }

      public bool Run(string name, bool result)
      {
        _executions.TryGetValue(name, out var count);
        _executions[name] = count + 1;
        return result;
      }
    }
  }

  /// <summary>
  /// Custom patterns apply per conjunct (the old engine consulted them per failed part).
  /// Lives in the DslPatternTests collection because the pattern registry is global.
  /// </summary>
  [Collection("DslPatternTests")]
  public class LogicalSplitCustomPatternTests : AssertionTestBase, IDisposable
  {
    public LogicalSplitCustomPatternTests()
    {
      Configuration.Patterns.Clear();
    }

    public void Dispose()
    {
      Configuration.Patterns.Clear();
    }

    [Fact]
    public void Custom_pattern_applies_to_a_failing_conjunct()
    {
      Configuration.Patterns.Register("none", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should not contain any items.",
          Actual = "It contained {instance.count} items.",
        },
      });

      var list = new List<string> { "a", "b", "c" };
      var x = 1;

      ShouldFail(() => list.None() && x == 1);
    }
  }
}
