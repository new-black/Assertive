namespace Assertive.Test.Generators
{
  using System;
  using System.Text.RegularExpressions;
  using Assertive.Runtime;
  using Xunit;
  using Assert = Xunit.Assert;
  using AssertiveAssert = Assertive.Assert;

  /// <summary>
  /// Tests for the delegate-based equality slice: whitelisted call sites are intercepted
  /// and produce decomposed EqualsPattern-parity output; everything else runs the plain
  /// delegate and reports from source text only.
  /// </summary>
  public class EqualsSliceTests
  {
    private static string StripAnsi(string input) => Regex.Replace(input, @"\[[0-9;]*[A-Za-z]", "");

    private static (Exception? Exception, bool WasIntercepted) Run(Action assert)
    {
      var before = GeneratedAssert.InterceptedCallCount;
      Exception? exception = null;

      try
      {
        assert();
      }
      catch (Exception ex)
      {
        exception = ex;
      }

      return (exception, GeneratedAssert.InterceptedCallCount > before);
    }

    private static (string Expected, string Actual) Decomposition(Exception exception)
    {
      return (
        StripAnsi(string.Join("\n", (string[])exception.Data["Assertive.Expected"]!)),
        StripAnsi(string.Join("\n", (string[])exception.Data["Assertive.Actual"]!)));
    }

    [Fact]
    public void Failing_equality_is_intercepted_with_decomposed_values()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => x.IndexOf('b') == expectedIndex));

      Assert.True(wasIntercepted);
      Assert.NotNull(exception);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("x.IndexOf('b'): 5", expected);
      Assert.Equal("x.IndexOf('b'): 3", actual);
    }

    [Fact]
    public void Passing_equality_is_intercepted_and_does_not_throw()
    {
      var x = "foobar";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => x.IndexOf('o') == 1));

      Assert.True(wasIntercepted);
      Assert.Null(exception);
    }

    [Fact]
    public void Not_equals_reports_should_not_equal()
    {
      var left = "same";
      var right = "same";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => left != right));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("left: should not equal \"same\".", expected);
      Assert.Equal("left: \"same\"", actual);
    }

    [Fact]
    public void Equals_method_form_is_intercepted()
    {
      var a = "A";
      var b = "B";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => a.Equals(b)));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("a: \"B\"", expected);
      Assert.Equal("a: \"A\"", actual);
    }

    [Fact]
    public void Custom_message_appears_in_output()
    {
      var actualValue = 41;
      var expectedValue = 42;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => actualValue == expectedValue, "the answer matters"));

      Assert.True(wasIntercepted);
      Assert.Contains("the answer matters", StripAnsi(exception!.Message));
      Assert.Contains("MESSAGE", StripAnsi(exception.Message));
    }

    [Fact]
    public void Enum_with_type_qualifier_is_intercepted()
    {
      var comparison = StringComparison.OrdinalIgnoreCase;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => comparison == StringComparison.Ordinal));

      Assert.True(wasIntercepted);

      var (expected, _) = Decomposition(exception!);
      Assert.Contains("comparison:", expected);
      Assert.Contains("Ordinal", expected);
    }

    [Fact]
    public void Exception_during_evaluation_reports_exception_section()
    {
      var user = new User();

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => user.Name.ToUpper() == "FOO"));

      Assert.True(wasIntercepted);
      Assert.NotNull(exception);

      var message = StripAnsi(exception!.Message);
      Assert.Contains("user.Name.ToUpper() == \"FOO\"", message);
      Assert.Contains("EXCEPTION", message);
      Assert.Contains("Object reference not set", message);
    }

    [Fact]
    public void Locals_that_are_not_whole_operands_are_listed()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var (exception, _) = Run(() => AssertiveAssert.That(() => x.IndexOf('b') == expectedIndex));

      var message = StripAnsi(exception!.Message);
      Assert.Contains("LOCALS", message);
      Assert.Contains("x:", message);
      // expectedIndex is the whole right operand; its value is already displayed.
      Assert.DoesNotContain("expectedIndex: 5", message);
    }

    [Fact]
    public void Null_comparison_is_not_intercepted_and_reports_source_text()
    {
      string? value = "not null";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => value == null));

      Assert.False(wasIntercepted);
      Assert.NotNull(exception);
      Assert.Contains("value == null", StripAnsi(exception!.Message));
      Assert.Empty((string[])exception.Data["Assertive.Expected"]!);
    }

    [Fact]
    public void Private_nested_operand_type_is_not_intercepted_but_still_works()
    {
      var secret = new Secret();

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => secret.Value == 4));

      Assert.False(wasIntercepted);
      Assert.NotNull(exception);
      Assert.Contains("secret.Value == 4", StripAnsi(exception!.Message));
    }

    private sealed class Secret
    {
      public int Value = 3;
    }
  }

  // Operand types must be nameable from the generated code, which lives at namespace level
  // in this assembly — hence internal top-level rather than nested in the test class.
  internal sealed class User
  {
    public string Name = null!;
  }
}
