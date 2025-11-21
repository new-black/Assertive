using System;
using System.Text.RegularExpressions;
using System.Threading;
using Assertive.Analyzers;
using Assertive.Config;
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
      ShouldFail(() => nullableInt == 1, "nullableInt: 1", "nullableInt: null");
      Assert.That(() => a != b);
      Assert.That(() => a == "A");
      Assert.That(() => x != y);
      Assert.That(() => x == 1);
      Assert.That(() => foo + bar == "foobar");
      
      ShouldFail(() => a == b, @"a: ""B""", @"a: ""A""");
      ShouldFail(() => nullableInt == x, "nullableInt: 1", "nullableInt: null");

      ShouldFail(() => x == y, "x: 2", "x: 1");
      ShouldFail(() => foo + bar == "barfoo",
        @"foo + bar: ""barfoo""", @"foo + bar: ""foobar""");
      ShouldFail(() => a == "B", @"a: ""B""", @"a: ""A""");
      ShouldFail(() => a.Equals(b), @"a: ""B""", @"a: ""A""");
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

      ShouldFail(() => a != b, @"a: should not equal ""A"".", @"a: ""A""");
      ShouldFail(() => x != y, "x: should not equal 1.", "x: 1");
      ShouldFail(() => foo + bar != "foobar",
        @"foo + bar: should not equal ""foobar"".", @"foo + bar: ""foobar""");
      ShouldFail(() => a != "A", @"a: should not equal ""A"".", @"a: ""A""");
      ShouldFail(() => !a.Equals(b), @"a: should not equal ""A"".", @"a: ""A""");
    }

    private (string a, string b) GetTuple()
    {
      return ("a", "b");
    }

    [Fact]
    public void Tuple_names_test_from_method()
    {
      ShouldFail(() => GetTuple().a == GetTuple().b, @"GetTuple().a: ""b""", @"GetTuple().a: ""a""");
    }

    [Fact]
    public void Tuple_names_test_from_local()
    {
      var s = (a: "foo", b: "bar");

      ShouldFail(() => s.a == s.b, @"s.a: ""bar""", @"s.a: ""foo""");
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
        ShouldFail(() => foo == s.bar, @"foo: ""bar""", @"foo: ""foo""");
      }
    }

    [Fact]
    public void EqualsPattern_is_triggered()
    {
      var a = "A";
      var b = "B";

      var failures = new AssertionFailureAnalyzer(new AssertionFailureContext(new Assertion(() => a == b, null, null), null)).AnalyzeAssertionFailures();
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
        @"a: { A = ""this is a string"" }", "a: { }");
    }

    [Fact]
    public void Enum_comparison_works()
    {
      var a = MyEnum.A;
      var b = MyEnum.B;

      ShouldFail(() => a == b, "a: MyEnum.B", "a: MyEnum.A");
    }

    [Fact]
    public void Enum_comparison_works_when_rhs_is_nullable()
    {
      var a = MyEnum.A;
      MyEnum? b = MyEnum.B;

      ShouldFail(() => a == b, "a: MyEnum.B", "a: MyEnum.A");

      b = null;
      
      ShouldFail(() => a == b, "a: null", "a: MyEnum.A");
    }

    [Fact]
    public void Nullable_Enum_comparison_works()
    {
      MyEnum? a = MyEnum.A;
      //MyEnum b = MyEnum.B;

      ShouldFail(() => a == MyEnum.B, "a: MyEnum.B", "a: MyEnum.A");
    }

    [Fact]
    public void Nullable_Enum_comparison_works_when_value_is_null()
    {
      MyEnum? a = null;
      //MyEnum b = MyEnum.B;

      ShouldFail(() => a == MyEnum.B, "a: MyEnum.B", "a: null");
    }

    [Fact]
    public void Enum_in_function_call_works()
    {
      ShouldFail(() => DoIt(MyEnum.A) == MyEnum.B,
        "DoIt(MyEnum.A): MyEnum.B", "DoIt(MyEnum.A): MyEnum.A");
    }

    [Fact]
    public void Nullable_bool_false()
    {
      bool? success = null;
      
      ShouldFail(() => success == false, "success: false", "success: null");
    }
    
    private MyEnum DoIt(MyEnum x)
    {
      return x;
    }

    [Fact]
    public void String_diff_shows_position_of_difference()
    {
      var actual = "Hello World";
      var expected = "Hallo World";

      try
      {
        Assert.That(() => actual == expected);
        Xunit.Assert.Fail("Should have failed");
      }
      catch (Exception ex)
      {
        Xunit.Assert.Contains("Strings differ at index", ex.Message);
        Xunit.Assert.Contains("[e]", ex.Message);
        Xunit.Assert.Contains("[a]", ex.Message);
      }
    }

    [Fact]
    public void String_diff_shows_difference_in_long_strings()
    {
      var actual = "The quick brown fox jumps over the lazy dog";
      var expected = "The quick brown cat jumps over the lazy dog";

      try
      {
        Assert.That(() => actual == expected);
        Xunit.Assert.Fail("Should have failed");
      }
      catch (Exception ex)
      {
        Xunit.Assert.Contains("Strings differ at index 16", ex.Message);
        Xunit.Assert.Contains("[f]", ex.Message);
        Xunit.Assert.Contains("[c]", ex.Message);
      }
    }

    [Fact]
    public void String_diff_shows_length_difference()
    {
      var actual = "Short";
      var expected = "Short string";

      try
      {
        Assert.That(() => actual == expected);
        Xunit.Assert.Fail("Should have failed");
      }
      catch (Exception ex)
      {
        Xunit.Assert.Contains("Strings differ in length", ex.Message);
      }
    }

    [Fact]
    public void String_diff_escapes_special_characters()
    {
      var actual = "Line 1\nLine 2\nLine 3";
      var expected = "Line 1\nLine 2\rLine 3";

      try
      {
        Assert.That(() => actual == expected);
        Xunit.Assert.Fail("Should have failed");
      }
      catch (Exception ex)
      {
        Xunit.Assert.True(ex.Message.Contains("\\n") || ex.Message.Contains("\\r"));
      }
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