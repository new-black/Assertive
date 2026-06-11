using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  internal record PropPatUser(string Name, int Age, string? Email);

  /// <summary>
  /// Assertions using C# 8+ property patterns: obj is User { Name: "Bob", Age: > 18 }.
  /// </summary>
  public class PropertyPatternTests : AssertionTestBase
  {
    // Type check fails
    [Fact]
    public void Reports_type_failure_when_object_is_wrong_type()
    {
      object obj = "not a user";
      ShouldFail(() => obj is PropPatUser { Name: "Bob" },
        "should be of type",
        "Type: string");
    }

    // Type check passes, property equality fails
    [Fact]
    public void Reports_property_equality_failure()
    {
      object obj = new PropPatUser("Alice", 30, null);
      ShouldFail(() => obj is PropPatUser { Name: "Bob" },
        "Bob",
        "Alice");
    }

    // Type check passes, property relational fails
    [Fact]
    public void Reports_property_relational_failure()
    {
      object obj = new PropPatUser("Bob", 15, null);
      ShouldFail(() => obj is PropPatUser { Age: > 18 }, "18", "15");
    }

    // Multiple properties, first fails
    [Fact]
    public void Reports_first_property_failure_in_multi_property_pattern()
    {
      object obj = new PropPatUser("Alice", 30, null);
      ShouldFail(() => obj is PropPatUser { Name: "Bob", Age: > 18 },
        "Bob",
        "Alice");
    }

    // Multiple properties, second fails
    [Fact]
    public void Reports_second_property_failure_in_multi_property_pattern()
    {
      object obj = new PropPatUser("Bob", 15, null);
      ShouldFail(() => obj is PropPatUser { Name: "Bob", Age: > 18 }, "18", "15");
    }

    // Property is-null check
    [Fact]
    public void Reports_null_property_failure_when_not_null()
    {
      object obj = new PropPatUser("Bob", 30, "bob@example.com");
      ShouldFail(() => obj is PropPatUser { Email: null }, "should be null", "bob@example.com");
    }

    // Property is-not-null check
    [Fact]
    public void Reports_not_null_property_failure_when_null()
    {
      object obj = new PropPatUser("Bob", 30, null);
      ShouldFail(() => obj is PropPatUser { Email: not null }, "should not be null", "null");
    }

    // Passing cases
    [Fact]
    public void Does_not_throw_when_assertion_passes()
    {
      object obj = new PropPatUser("Bob", 30, "bob@example.com");
      Assert(() => obj is PropPatUser { Name: "Bob" });
      Assert(() => obj is PropPatUser { Name: "Bob", Age: > 18 });
      Assert(() => obj is PropPatUser { Email: not null });
    }

    // In a &&-chain (type var declared before, property pattern as second conjunct)
    [Fact]
    public void Property_pattern_in_conjunction_reports_correct_conjunct()
    {
      object obj = new PropPatUser("Alice", 30, null);
      var enabled = true;
      ShouldFail(() => enabled && obj is PropPatUser { Name: "Bob" },
        "Bob",
        "Alice");
    }
  }
}
