using System;
using System.Linq;
using System.Text.RegularExpressions;
using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public  class EqualsPatternTests : AssertionTestBase
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

    private static int Triple(int value) => value * 3;

    [Fact]
    public void Private_static_method_operands_are_reinvoked_reflectively()
    {
      // Inaccessible to generated code, so re-evaluation goes through
      // GeneratedAssert.InvokeStatic (name + argument count resolution).
      ShouldFail(() => Triple(2) == 7, "Triple(2): 7", "Triple(2): 6");
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

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);
        Xunit.Assert.Contains("String diff (expected vs actual):", message);
        Xunit.Assert.Contains("Legend:", message);
        Xunit.Assert.Contains("- [E1] H[-a-]llo World", message);
        Xunit.Assert.Contains("+ [A1] H[+e+]llo World", message);
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void EqualsPattern_without_colors_uses_plain_messages()
    {
      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var actual = "apple";
        var expected = "orange";

        ShouldFail(() => actual == expected,
          @"actual: ""orange""",
          @"actual: ""apple""");
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void EqualsPattern_without_colors_handles_long_strings()
    {
      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var actual = "This is a long string with apple inside.";
        var expected = "This is a long string with orange inside.";

        ShouldFail(() => actual == expected,
          @"actual: ""This is a long string with orange inside.""",
          @"actual: ""This is a long string with apple inside.""");
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void String_diff_shows_difference_in_long_strings()
    {
      var actual = """
                   The quick brown fox jumps over the lazy dog
                   The quick brown fox jumps over the lazy dog
                   The quick brown fox jumps over the lazy dog
                   """;
      var expected = """
                     The quick brown fox jumps over the lazy dog
                     The quick brown cat jumps over the lazy god
                     The quick brown fox jumps over the lazy dog
                     """;
      
      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);
        Xunit.Assert.Contains("String diff (expected vs actual):", message);
        Xunit.Assert.Contains("cat jumps over the lazy god", message);
        Xunit.Assert.Contains("fox jumps over the lazy dog", message);
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void String_diff_shows_length_difference()
    {
      var actual = "Short";
      var expected = "Short string";

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);
        Xunit.Assert.Contains("String diff (expected vs actual):", message);
        Xunit.Assert.Contains("Legend:", message);
        Xunit.Assert.Contains("- [E1] Shor[-t s-]t[-ring-]", message);
        Xunit.Assert.Contains("+ [A1] Short", message);
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void EqualsPattern_full_output_without_colors_matches_expected()
    {
      var actual = "ABCDEFGHIJK";
      var expected = "ABXXEFGHIJK";

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);

        var expectedMessage = """

actual == expected

[EXPECTED]
actual: "ABXXEFGHIJK"
[ACTUAL]
actual: "ABCDEFGHIJK"
String diff (expected vs actual):
Legend: [E#] expected line, [A#] actual line, plain line number = unchanged
- [E1] AB[-XX-]EFGHIJK
+ [A1] AB[+CD+]EFGHIJK


················································································

""";

        Xunit.Assert.Equal(expectedMessage, message);
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void String_diff_limits_context_for_long_lines()
    {
      var actual = "Start " +
                   new string('a', 60) +
                   " MIDDLE_one " +
                   new string('b', 60) +
                   " MIDDLE_two " +
                   new string('c', 60) +
                   " End";
      var expected = "Start " +
                     new string('a', 60) +
                     " middle_one " +
                     new string('b', 60) +
                     " middle-two " +
                     new string('c', 60) +
                     " End";

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);

        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expectedLine = lines.First(l => l.StartsWith("- [E1]"));
        var actualLine = lines.First(l => l.StartsWith("+ [A1]"));

        // We should show both differences with trimmed context (no full 60-char runs).
        Xunit.Assert.True(expectedLine.Split("...", StringSplitOptions.None).Length - 1 >= 2);
        Xunit.Assert.True(actualLine.Split("...", StringSplitOptions.None).Length - 1 >= 2);

        Xunit.Assert.Contains("middle", expectedLine, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("one", expectedLine, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("two", expectedLine, StringComparison.OrdinalIgnoreCase);

        Xunit.Assert.Contains("middle", actualLine, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("one", actualLine, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("two", actualLine, StringComparison.OrdinalIgnoreCase);

        Xunit.Assert.DoesNotContain(new string('a', 40), expectedLine);
        Xunit.Assert.DoesNotContain(new string('b', 40), expectedLine);
        Xunit.Assert.DoesNotContain(new string('c', 40), expectedLine);
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_diff_limits_context_for_many_lines(bool colorsEnabled)
    {
      var actualLines = Enumerable.Range(1, 60).Select(i => $"Line {i}").ToArray();
      actualLines[5] = "Line six (actual)";
      actualLines[30] = "Line thirty-one (actual)";
      actualLines[FiftyFiveIndex()] = "Line fifty-six (actual)";

      var expectedLines = Enumerable.Range(1, 60).Select(i => $"Line {i}").ToArray();
      expectedLines[5] = "Line six (expected)";
      expectedLines[30] = "Line thirty-one (expected)";
      expectedLines[FiftyFiveIndex()] = "Line fifty-six (expected)";

      var actual = string.Join("\n", actualLines);
      var expected = string.Join("\n", expectedLines);

      var originalColors = Configuration.Colors.Enabled;

      Configuration.Colors.Enabled = colorsEnabled;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);

        var diffBlock = message.Split("String diff (expected vs actual):", StringSplitOptions.None)[1];
        var lines = diffBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert(() => lines.Any(l => l.Contains("Line six", StringComparison.OrdinalIgnoreCase)));
        Assert(() => lines.Any(l => l.Contains("Line thirty-one", StringComparison.OrdinalIgnoreCase)));
        Assert(() => lines.Any(l => l.Contains("Line fifty-six", StringComparison.OrdinalIgnoreCase)));

        // Ensure we didn't dump all intervening unchanged lines
        Assert(() => lines.Any(l => l.Contains("...")));
        Assert(() => !lines.Any(l => l.Contains("Line 1")));
        Assert(() => !lines.Any(l => l.Contains("Line 60")));
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }

      static int FiftyFiveIndex() => 55 - 1; // zero-based index for 55
    }

    [Fact]
    public void String_diff_escapes_special_characters()
    {
      var actual = "Line 1\nLine 2\nLine 3";
      var expected = "Line 1\nLine 2\rLine 3";

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        Xunit.Assert.Contains("String diff (expected vs actual):", ex.Message);
        Xunit.Assert.Contains("\\r", ex.Message);
        Xunit.Assert.Contains("\\n", ex.Message);
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void String_diff_truncates_long_unchanged_lines()
    {
      var longLine = "Prefix " + new string('x', 100) + " Suffix";
      var actual = longLine + "\nChanged line actual\n" + longLine;
      var expected = longLine + "\nChanged line expected\n" + longLine;

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;

      try
      {
        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert.That(() => actual == expected));
        var message = StripAnsi(ex.Message);

        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unchangedLines = lines.Where(l => l.StartsWith("1:") || l.StartsWith("3:")).ToList();

        // Unchanged lines should be truncated and contain ellipsis
        Assert(() => unchangedLines.Count >= 2);
        foreach (var line in unchangedLines)
        {
          Assert(() => line.Contains("..."));
          Assert(() => line.Contains("Prefix"));
          Assert(() => line.Contains("Suffix"));
          // Should not contain the full 100-char run of x's
          Assert(() => !line.Contains(new string('x', 50)));
        }
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    // Regression tests for unbounded O(m*n) memory growth in StringDiffHelper. Without the cell-count
    // guards, these inputs would attempt to allocate hundreds of GB and OOM. With the guards they must
    // complete quickly using linear memory and still produce a (degraded) diff.

    [Fact]
    public void String_diff_does_not_exhaust_memory_for_huge_single_line()
    {
      // A single line with no newlines defeats line-level splitting and forces char-level alignment.
      // 300k x 300k chars would be ~360GB of int matrix without the guard.
      var actual = new string('a', 300_000) + "X" + new string('a', 300_000);
      var expected = new string('a', 300_000) + "Y" + new string('a', 300_000);

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var diff = Helpers.StringDiffHelper.GetStringDiff(actual, expected);
        sw.Stop();

        Assert(() => diff.Contains("String diff (expected vs actual):"));
        // Falls back to whole-line removed/added rendering rather than inline char alignment.
        Assert(() => diff.Contains("[-"));
        Assert(() => diff.Contains("[+"));
        Assert(() => sw.Elapsed < TimeSpan.FromSeconds(5));
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void String_diff_does_not_exhaust_memory_for_huge_number_of_lines()
    {
      // 50k x 50k lines would be ~10GB of int matrix without the guard.
      var actualLines = Enumerable.Range(1, 50_000).Select(i => $"Line {i}").ToArray();
      var expectedLines = Enumerable.Range(1, 50_000).Select(i => $"Line {i}").ToArray();
      actualLines[25_000] = "Changed actual";
      expectedLines[25_000] = "Changed expected";

      var actual = string.Join("\n", actualLines);
      var expected = string.Join("\n", expectedLines);

      var originalColors = Configuration.Colors.Enabled;
      Configuration.Colors.Enabled = false;
      try
      {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var diff = Helpers.StringDiffHelper.GetStringDiff(actual, expected);
        sw.Stop();

        Assert(() => diff.Contains("String diff (expected vs actual):"));
        // Degrades to the positional (non-LCS) comparison and says so.
        Assert(() => diff.Contains("positional", StringComparison.OrdinalIgnoreCase));
        // The differing line is still surfaced (inline-diffed, so the prefix stays contiguous).
        Assert(() => diff.Contains("Changed"));
        Assert(() => diff.Contains("[-"));
        Assert(() => diff.Contains("[+"));
        // Unchanged runs far from the diff are collapsed rather than dumped wholesale.
        Assert(() => diff.Contains("..."));
        Assert(() => sw.Elapsed < TimeSpan.FromSeconds(5));
      }
      finally
      {
        Configuration.Colors.Enabled = originalColors;
      }
    }

    [Fact]
    public void Closest_substring_diff_is_bounded_for_huge_inputs()
    {
      // 1M x 1M chars would be ~4TB of int matrix without the existing guard; should bail out to null.
      var haystack = new string('a', 1_000_000);
      var needle = new string('b', 1_000_000);

      var sw = System.Diagnostics.Stopwatch.StartNew();
      var result = Helpers.StringDiffHelper.GetClosestSubstringDiff(haystack, needle);
      sw.Stop();

      Assert(() => result == null);
      Assert(() => sw.Elapsed < TimeSpan.FromSeconds(5));
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
