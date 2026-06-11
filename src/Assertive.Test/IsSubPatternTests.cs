using System;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Assertions using C# 9+ is-sub-patterns: relational, constant, null, and-patterns.
  /// </summary>
  public class IsSubPatternTests : AssertionTestBase
  {
    // --- Relational sub-patterns (n is > 18) ---

    [Fact]
    public void Reports_relational_greater_than_failure()
    {
      var n = 5;
      ShouldFail(() => n is > 18, "18", "5");
    }

    [Fact]
    public void Reports_relational_less_than_failure()
    {
      var n = 100;
      ShouldFail(() => n is < 50, "50", "100");
    }

    [Fact]
    public void Reports_relational_greater_than_or_equal_failure()
    {
      var n = 3;
      ShouldFail(() => n is >= 10, "10", "3");
    }

    [Fact]
    public void Reports_relational_less_than_or_equal_failure()
    {
      var n = 99;
      ShouldFail(() => n is <= 10, "10", "99");
    }

    [Fact]
    public void Passing_relational_does_not_throw()
    {
      var n = 42;
      Assert(() => n is > 0);
      Assert(() => n is >= 42);
      Assert(() => n is < 100);
      Assert(() => n is <= 42);
    }

    // --- and-pattern (n is >= 0 and <= 100) ---

    [Fact]
    public void Reports_and_lower_bound_failure()
    {
      var n = -5;
      ShouldFail(() => n is >= 0 and <= 100, "0", "-5");
    }

    [Fact]
    public void Reports_and_upper_bound_failure()
    {
      var n = 150;
      ShouldFail(() => n is >= 0 and <= 100, "100", "150");
    }

    [Fact]
    public void Passing_and_pattern_does_not_throw()
    {
      var n = 50;
      Assert(() => n is >= 0 and <= 100);
    }

    [Fact]
    public void And_pattern_in_conjunction_reports_correct_conjunct()
    {
      var n = 150;
      var enabled = true;
      ShouldFail(() => enabled && n is >= 0 and <= 100, "100", "150");
    }

    // --- is null / is not null ---

    [Fact]
    public void Reports_is_null_failure_when_not_null()
    {
      string? s = "hello";
      ShouldFail(() => s is null,
        "null",
        "hello");
    }

    [Fact]
    public void Reports_is_not_null_failure_when_null()
    {
      string? s = null;
      ShouldFail(() => s is not null, "should not be null", "null");
    }

    [Fact]
    public void Passing_is_null_and_is_not_null_do_not_throw()
    {
      string? s1 = null;
      string? s2 = "hello";
      Assert(() => s1 is null);
      Assert(() => s2 is not null);
    }

    // --- is constant (n is 42) ---

    [Fact]
    public void Reports_constant_equality_failure()
    {
      var n = 7;
      ShouldFail(() => n is 42,
        "42",
        "7");
    }

    [Fact]
    public void Reports_not_constant_failure()
    {
      var n = 42;
      ShouldFail(() => n is not 42, "should not equal", "42");
    }

    [Fact]
    public void Passing_constant_patterns_do_not_throw()
    {
      var n = 42;
      Assert(() => n is 42);
      Assert(() => n is not 99);
    }
  }
}
