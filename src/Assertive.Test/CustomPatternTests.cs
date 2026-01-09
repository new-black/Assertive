using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assertive.Config;
using Assertive.Plugin;
using Xunit;

namespace Assertive.Test
{
  [Collection("DslPatternTests")]
  public class CustomPatternTests : AssertionTestBase, System.IDisposable
  {
    public CustomPatternTests()
    {
      // Reset patterns before each test
      Configuration.Patterns.Clear();
    }

    public void Dispose()
    {
      // Reset patterns after each test
      Configuration.Patterns.Clear();
    }

    [Fact]
    public void Simple_method_pattern_matches()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should not contain any items.",
          Actual = "It contained {instance.count} items."
        }
      });

      var list = new List<string> { "a", "b", "c" };

      ShouldFail(() => list.None(), "Collection list should not contain any items.", "It contained 3 items.");
    }

    [Fact]
    public void Pattern_with_negation_uses_alternate_output()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should not contain any items.",
          Actual = "It contained {instance.count} items."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "Collection {instance} should contain at least one item.",
          Actual = "It was empty."
        }
      });

      var list = new List<string>();

      // {instance} is replaced with "list" in the actual output
      ShouldFail(() => !list.None(), "Collection list should contain at least one item.", "It was empty.");
    }

    [Fact]
    public void Pattern_without_negation_still_matches_non_negated()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "NoneStrict" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should not contain any items.",
          Actual = "It contained {instance.count} items."
        }
      });

      var list = new List<string> { "a", "b" };

      // Non-negated expression should match
      ShouldFail(() => list.NoneStrict(), "Collection list should not contain any items.", "It contained 2 items.");
    }

    [Fact]
    public void Multiple_patterns_can_be_registered()
    {
      Configuration.Patterns.Register("None", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should be empty.",
          Actual = "It had {instance.count} items."
        }
      });

      Configuration.Patterns.Register("IsEmpty", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "IsEmpty" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "{instance} should be empty.",
          Actual = "It was not empty."
        }
      });

      var list = new List<string> { "a" };

      ShouldFail(() => list.None(), "Collection list should be empty.", "It had 1 items.");
      // {instance} is replaced with "list" in the actual output
      ShouldFail(() => list.IsEmpty(), "list should be empty.", "It was not empty.");
    }

    [Fact]
    public void Pattern_with_declaring_type_constraint()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "None" } },
          new MatchPredicate { DeclaringType = "TestExtensions" }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Custom: {instance} should be empty.",
          Actual = "It had items."
        }
      });

      var list = new List<string> { "a" };

      ShouldFail(() => list.None(), "Custom: list should be empty.", "It had items.");
    }

    [Fact]
    public void Reset_clears_all_patterns()
    {
      // First register a pattern and verify it works
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Custom message for None.",
          Actual = "Custom actual."
        }
      });

      var list = new List<string> { "a" };
      ShouldFail(() => list.None(), "Custom message for None.", "Custom actual.");

      // Reset should clear patterns
      Configuration.Patterns.Clear();

      // Register a different pattern to prove reset worked
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Different message after reset.",
          Actual = "Different actual."
        }
      });

      ShouldFail(() => list.None(), "Different message after reset.", "Different actual.");
    }

    [Fact]
    public void Pattern_with_instance_type_constraint()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "None" } },
          new MatchPredicate { InstanceType = "List" }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "List {instance} of type {instance.type} should be empty.",
          Actual = "It had {instance.count} items."
        }
      });

      var list = new List<string> { "a" };

      ShouldFail(() => list.None(), "List list of type List<String> should be empty.", "It had 1 items.");
    }

    [Fact]
    public void Pattern_with_namespace_constraint()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "None" } },
          new MatchPredicate { Namespace = "Assertive.Test" }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Namespaced: {instance} should be empty.",
          Actual = "It was not."
        }
      });

      var list = new List<string> { "a" };

      ShouldFail(() => list.None(), "Namespaced: list should be empty.", "It was not.");
    }

    [Fact]
    public void Pattern_with_parameter_count_constraint()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "HasExactly", ParameterCount = 1 } }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "{instance} should have exactly {arg0} items.",
          Actual = "It had {instance.count} items."
        }
      });

      var list = new List<string> { "a", "b" };

      ShouldFail(() => list.HasExactly(3), "list should have exactly 3 items.", "It had 2 items.");
    }

    [Fact]
    public void Pattern_with_extension_method_constraint()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "None", IsExtension = true } }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Extension method: {instance} should be empty.",
          Actual = "It was not."
        }
      });

      var list = new List<string> { "a" };

      ShouldFail(() => list.None(), "Extension method: list should be empty.", "It was not.");
    }

    [Fact]
    public void Pattern_with_argument_value_placeholder()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "HasExactly" } }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "{instance} should have exactly {arg0.value} items.",
          Actual = "It had {instance.count}."
        }
      });

      var list = new List<string> { "a" };
      var expected = 5;

      ShouldFail(() => list.HasExactly(expected), "list should have exactly 5 items.", "It had 1.");
    }

    [Fact]
    public void Property_pattern_matches()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Property = new PropertyMatch { Name = "IsValid" } }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "{instance} should be valid.",
          Actual = "IsValid was {value}."
        }
      });

      var obj = new TestObject { IsValid = false };

      ShouldFail(() => obj.IsValid, "obj should be valid.", "IsValid was False.");
    }

    [Fact]
    public void Property_pattern_with_negation()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Property = new PropertyMatch { Name = "IsValid" } }
        ],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should be valid.",
          Actual = "IsValid was {value}."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not be valid.",
          Actual = "IsValid was {value}."
        }
      });

      var obj = new TestObject { IsValid = true };

      ShouldFail(() => !obj.IsValid, "obj should not be valid.", "IsValid was True.");
    }

    [Fact]
    public void Property_pattern_with_instance_type()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Property = new PropertyMatch { Name = "IsValid" } },
          new MatchPredicate { InstanceType = "TestObject" }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "TestObject {instance} should be valid.",
          Actual = "It was not."
        }
      });

      var obj = new TestObject { IsValid = false };

      ShouldFail(() => obj.IsValid, "TestObject obj should be valid.", "It was not.");
    }

    [Fact]
    public void Registering_pattern_with_same_name_replaces_existing()
    {
      // Register first pattern
      Configuration.Patterns.Register("MyPattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "First message.",
          Actual = "First actual."
        }
      });

      var list = new List<string> { "a" };
      ShouldFail(() => list.None(), "First message.", "First actual.");

      // Register pattern with same name - should replace
      Configuration.Patterns.Register("MyPattern", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Replaced message.",
          Actual = "Replaced actual."
        }
      });

      ShouldFail(() => list.None(), "Replaced message.", "Replaced actual.");
    }

    [Fact]
    public void Unregister_removes_pattern_by_name()
    {
      Configuration.Patterns.Register("ToRemove", new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "Custom message.",
          Actual = "Custom actual."
        }
      });

      var list = new List<string> { "a" };
      ShouldFail(() => list.None(), "Custom message.", "Custom actual.");

      // Unregister should return true for existing pattern
      var result = Configuration.Patterns.Unregister("ToRemove");
      Assert.That(() => result == true);

      // Unregister should return false for non-existing pattern
      var result2 = Configuration.Patterns.Unregister("NonExistent");
      Assert.That(() => result2 == false);
    }

    [Fact]
    public void Pattern_with_floating_point_argument_uses_invariant_culture()
    {
      Configuration.Patterns.Register("test-pattern", new PatternDefinition
      {
        Match =
        [
          new MatchPredicate { Method = new MethodMatch { Name = "IsGreaterThan" } }
        ],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "{instance} should be greater than {arg0.value}.",
          Actual = "It was {instance.value}."
        }
      });

      var originalCulture = CultureInfo.CurrentCulture;

      try
      {
        // Set culture to German which uses comma as decimal separator
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        double value = 3.1;
        double threshold = 5.5;

        // Without the InvariantCulture fix, this would show "5,5" instead of "5.5"
        ShouldFail(
          () => value.IsGreaterThan(threshold),
          "value should be greater than 5.5.",  // Period, not comma
          "It was 3.1."  // Period, not comma
        );
      }
      finally
      {
        CultureInfo.CurrentCulture = originalCulture;
      }
    }
  }

  public class TestObject
  {
    public bool IsValid { get; set; }
  }

  /// <summary>
  /// Test extension methods for DSL pattern testing.
  /// </summary>
  public static class TestExtensions
  {
    public static bool None<T>(this IEnumerable<T> source) => !source.Any();
    public static bool NoneStrict<T>(this IEnumerable<T> source) => !source.Any();
    public static bool NoneForReset<T>(this IEnumerable<T> source) => !source.Any();
    public static bool IsEmpty<T>(this IEnumerable<T> source) => !source.Any();
    public static bool HasExactly<T>(this IEnumerable<T> source, int count) => source.Count() == count;
    public static bool IsGreaterThan(this double value, double threshold) => value > threshold;
  }
}
