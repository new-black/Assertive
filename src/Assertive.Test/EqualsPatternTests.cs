using System;
using System.Text.RegularExpressions;
using System.Threading;
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
      int? nullableInt = null;
      ShouldFail(() => nullableInt == 1, "Expected nullableInt to equal 1 but nullableInt was null.");
      Assert.That(() => a != b);
      Assert.That(() => a == "A");
      Assert.That(() => x != y);
      Assert.That(() => x == 1);
      Assert.That(() => foo + bar == "foobar");

      ShouldFail(() => a == b, @"Expected a to equal b but a was ""A"" while b was ""B"".");
      ShouldFail(() => nullableInt == x, "Expected nullableInt to equal x but nullableInt was null while x was 1.");

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

    private (string a, string b) GetTuple()
    {
      return ("a", "b");
    }

    [Fact]
    public void Tuple_names_test_from_method()
    {
      ShouldFail(() => GetTuple().a == GetTuple().b, @"Expected GetTuple().a to equal GetTuple().b but GetTuple().a was ""a"" while GetTuple().b was ""b"".");
    }

    [Fact]
    public void Tuple_names_test_from_local()
    {
      var s = (a: "foo", b: "bar");

      ShouldFail(() => s.a == s.b, @"Expected s.a to equal s.b but s.a was ""foo"" while s.b was ""bar"".");
    }

    [Fact]
    public void Locals_in_foreach_test()
    {
      var strings = new[]
      {
        ("foo", bar: "bar")
      };

      foreach (var s in strings)
      {
        var foo = s.Item1;
        ShouldFail(() => foo == s.bar, @"Expected foo to equal s.bar but foo was ""foo"" while s.bar was ""bar"".");
      }
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
        A = "this is a string"
      };

      ShouldFail(() => a.Equals(b),
        "Expected a to equal b but a was { A = null, B = null } while b was { A = this is a string, B = null }.");
    }

    [Fact]
    public void Enum_comparison_works()
    {
      var a = MyEnum.A;
      var b = MyEnum.B;

      ShouldFail(() => a == b, "Expected a to equal b but a was MyEnum.A while b was MyEnum.B.");
    }

    [Fact]
    public void Nullable_Enum_comparison_works()
    {
      MyEnum? a = MyEnum.A;
      //MyEnum b = MyEnum.B;

      ShouldFail(() => a == MyEnum.B, "Expected a to equal MyEnum.B but a was MyEnum.A.");
    }

    [Fact]
    public void Nullable_Enum_comparison_works_when_value_is_null()
    {
      MyEnum? a = null;
      //MyEnum b = MyEnum.B;

      ShouldFail(() => a == MyEnum.B, "Expected a to equal MyEnum.B but a was null.");
    }

    [Fact]
    public void Enum_in_function_call_works()
    {
      ShouldFail(() => DoIt(MyEnum.A) == MyEnum.B,
        "Expected DoIt(MyEnum.A) to equal MyEnum.B but DoIt(MyEnum.A) was MyEnum.A.");
    }

    private MyEnum DoIt(MyEnum x)
    {
      return x;
    }

    private enum MyEnum
    {
      A = 1,
      B = 2
    }

    private struct MyStruct
    {
      public string A { get; set; }
      public string B { get; set; }
    }
  }
}