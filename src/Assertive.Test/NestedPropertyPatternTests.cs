using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  internal record NppPerson(string Name, NppAddress? Address, NppContact? Contact);
  internal record NppAddress(string City, string Country);
  internal record NppContact(string? Email, string? Phone);

  /// <summary>
  /// Assertions using nested property patterns: obj is Person { Address: { City: "NYC" } }.
  /// The generator should walk the nested pattern tree and emit a leaf per property check,
  /// including the outer type-check leaf and each level of nesting.
  /// </summary>
  public class NestedPropertyPatternTests : AssertionTestBase
  {
    [Fact]
    public void Reports_nested_property_equality_failure()
    {
      var person = new NppPerson("Bob", new NppAddress("London", "UK"), null);
      ShouldFail(() => person is { Address: { City: "NYC" } });
    }

    [Fact]
    public void Reports_outer_null_when_nested_path_is_null()
    {
      var person = new NppPerson("Bob", null, null);
      ShouldFail(() => person is { Address: { City: "NYC" } });
    }

    [Fact]
    public void Reports_failure_in_deeply_nested_pattern()
    {
      var person = new NppPerson("Bob", new NppAddress("NYC", "Germany"), null);
      ShouldFail(() => person is { Address: { City: "NYC", Country: "US" } });
    }

    [Fact]
    public void Reports_sibling_nested_failure()
    {
      var person = new NppPerson("Alice", new NppAddress("NYC", "US"), null);
      ShouldFail(() => person is { Name: "Bob", Address: { City: "NYC" } });
    }

    [Fact]
    public void Nested_pattern_in_conjunction()
    {
      var person = new NppPerson("Bob", new NppAddress("London", "UK"), null);
      var active = true;
      ShouldFail(() => active && person is { Address: { City: "NYC" } });
    }

    [Fact]
    public void Passing_nested_pattern_does_not_throw()
    {
      var person = new NppPerson("Bob", new NppAddress("NYC", "US"), null);
      Assert(() => person is { Address: { City: "NYC", Country: "US" } });
    }

    [Fact]
    public void Three_levels_of_nesting()
    {
      var person = new NppPerson("Bob", new NppAddress("NYC", "US"),
        new NppContact("wrong@example.com", null));
      ShouldFail(() => person is { Contact: { Email: "bob@example.com" } });
    }
  }
}
