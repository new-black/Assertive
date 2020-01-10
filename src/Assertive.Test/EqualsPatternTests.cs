using System.ComponentModel.DataAnnotations;
using Assertive.Patterns;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class EqualsPatternTests : AssertionTestBase
  {
    [Fact]
    public void EqualsPattern_tests()
    {
      var a = "A";
      var b = "B";

      var x = 1;
      var y = 2;

      var foo = "foo";
      var bar = "bar";

      Assert.That(() => a != b);
      Assert.That(() => a == "A");
      Assert.That(() => x != y);
      Assert.That(() => x == 1);
      Assert.That(() => foo + bar == "foobar");
      
      ShouldFail(() => a == b, @"Expected a to equal b but a was ""A"" while b was ""B"".");
      ShouldFail(() => x == y, "Expected x to equal y but x was 1 while y was 2.");
      ShouldFail(() => foo + bar == "barfoo",
        @"Expected foo + bar to equal ""barfoo"" but foo + bar was ""foobar"".");
      ShouldFail(() => a == "B", @"Expected a to equal ""B"" but a was ""A"".");
      ShouldFail(() => a.Equals(b), @"Expected a to equal b but a was ""A"" while b was ""B"".");
    }
    
    [Fact]
    public void Not_equals_works()
    {
      var a = "A";
      var b = "A";

      var x = 1;
      var y = 1;

      var foo = "foo";
      var bar = "bar";

      ShouldFail(() => a != b, @"Expected a to not equal b but they were equal (value: ""A"").");
      ShouldFail(() => x != y, @"Expected x to not equal y but they were equal (value: 1).");
      ShouldFail(() => foo + bar != "foobar",
        @"Expected foo + bar to not equal ""foobar"".");
      ShouldFail(() => a != "A", @"Expected a to not equal ""A"".");
      ShouldFail(() => !a.Equals(b), @"Expected a to not equal b but they were equal (value: ""A"").");
    }
    
    [Fact]
    public void EqualsPattern_is_triggered()
    {
      var a = "A";
      var b = "B";

      var failures = new AssertionFailureAnalyzer(() => a == b, null).AnalyzeAssertionFailures();
      Assert(() => failures.Count == 1 && failures[0].FriendlyMessagePattern is EqualsPattern);
    }

    [Fact]
    public void EqualsPattern_is_triggered_for_struct()
    {
      var a = new MyStruct();
      var b = new MyStruct()
      {
        A = "1"
      };
      
      ShouldFail(() => a.Equals(b), "Expected a to equal b but a was Assertive.Test.EqualsPatternTests+MyStruct while b was Assertive.Test.EqualsPatternTests+MyStruct.");
    }

    private struct MyStruct
    {
      public string A { get; set; }
      public string B { get; set; }
    }
  }
}