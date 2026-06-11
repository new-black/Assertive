using System;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  internal record PatternUser(string Name, int Age);
  internal record PatternAdmin(string Name, int Level);

  /// <summary>
  /// Assertions containing C# 7+ is-pattern expressions with variable declarations.
  /// In a &&-only body, the pattern variable declared in one conjunct (obj is PatternUser u) is
  /// available for decomposition in subsequent conjuncts (u.Name == "Bob").
  /// </summary>
  public class IsPatternVariableTests : AssertionTestBase
  {

    // Scenario: is-check fails (obj is not a PatternUser at all)
    [Fact]
    public void Reports_is_failure_when_type_check_fails()
    {
      object obj = "not a user";

      ShouldFail(() => obj is PatternUser u && u.Name == "Bob",
        "should be of type",
        "Type: string");
    }

    // Scenario: is-check passes but the subsequent property equality fails
    [Fact]
    public void Reports_property_equality_failure_when_type_check_passes()
    {
      object obj = new PatternUser("Alice", 30);

      ShouldFail(() => obj is PatternUser u && u.Name == "Bob",
        "Bob",
        "Alice");
    }

    // Comparison on the pattern variable
    [Fact]
    public void Reports_comparison_failure_on_pattern_variable_property()
    {
      object obj = new PatternUser("Bob", 5);

      ShouldFail(() => obj is PatternUser u && u.Age > 18,
        "18",
        "5");
    }

    // Three conjuncts: is-check, property equality, then another comparison
    [Fact]
    public void Reports_third_conjunct_failure_with_two_preceding_checks()
    {
      object obj = new PatternUser("Bob", 5);

      ShouldFail(() => obj is PatternUser u && u.Name == "Bob" && u.Age > 18,
        "18",
        "5");
    }

    // The is-check in the second conjunct (first is a plain bool check)
    [Fact]
    public void Pattern_variable_in_second_conjunct_with_prior_plain_check()
    {
      object obj = new PatternUser("Alice", 30);
      var enabled = true;

      ShouldFail(() => enabled && obj is PatternUser u && u.Name == "Bob",
        "Bob",
        "Alice");
    }

    // Pattern variable of a value type
    [Fact]
    public void Reports_failure_for_value_type_pattern_variable()
    {
      object obj = 42;

      ShouldFail(() => obj is int n && n > 100,
        "100",
        "42");
    }

    // Whole body passes
    [Fact]
    public void Does_not_throw_when_assertion_passes()
    {
      object obj = new PatternUser("Bob", 30);
      Assert(() => obj is PatternUser u && u.Name == "Bob");
    }

    // Is-pattern alone (no variable) is still classified as Is kind
    [Fact]
    public void Standalone_is_pattern_with_variable_is_classified_as_is_kind()
    {
      object obj = new PatternAdmin("Charlie", 1);

      ShouldFail(() => obj is PatternUser u,
        "should be of type",
        "Type: PatternAdmin");
    }

    // Pattern variable used in null check via a subsequent conjunct
    [Fact]
    public void Reports_null_check_on_pattern_variable_property()
    {
      object obj = new PatternUser(null!, 30);

      ShouldFail(() => obj is PatternUser u && u.Name != null,
        "Name",
        "null");
    }

    // Cross-check: body that uses pattern var only in the is-leaf itself doesn't break
    [Fact]
    public void Is_check_with_discard_still_degrades_gracefully()
    {
      object obj = "not a user";

      // `obj is PatternUser _` has no variable; the is-leaf uses DiscardDesignation.
      // It should classify as Is kind (same as obj is PatternUser).
      ShouldFail(() => (object)"foo" is PatternUser _,
        "should be of type",
        "Type: string");
    }
  }
}
