namespace Assertive.Test.Generators
{
  using System;
  using System.Linq;
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
    public void Null_comparison_is_intercepted_as_null_pattern()
    {
      string? value = "not null";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => value == null));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("value should be null.", expected);
      Assert.Equal("\"not null\"", actual);
    }

    [Fact]
    public void Logical_and_is_intercepted_opaquely_and_reports_source_text()
    {
      var value = "ab";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => value.Contains('a') && value.Contains('z')));

      // Not a decomposable form, but still intercepted (opaquely): source text, no
      // expected/actual decomposition; the exception path gets cause attribution.
      Assert.True(wasIntercepted);
      Assert.NotNull(exception);
      Assert.Contains("value.Contains('a') && value.Contains('z')", StripAnsi(exception!.Message));
      Assert.Empty((string[])exception.Data["Assertive.Expected"]!);
    }

    [Fact]
    public void Private_nested_operand_type_is_intercepted_reflectively()
    {
      var secret = new Secret();

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => secret.Value == 4));

      Assert.True(wasIntercepted);
      Assert.NotNull(exception);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("secret.Value: 4", expected);
      Assert.Equal("secret.Value: 3", actual);
    }

    [Fact]
    public void Private_nested_enum_comparison_is_intercepted_reflectively()
    {
      var a = Mood.Happy;
      var b = Mood.Grumpy;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => a == b));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("a: Mood.Grumpy", expected);
      Assert.Equal("a: Mood.Happy", actual);
    }

    [Fact]
    public void Private_nested_enum_constant_is_resolved_reflectively()
    {
      Mood? a = null;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => a == Mood.Grumpy));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("a: Mood.Grumpy", expected);
      Assert.Equal("a: null", actual);
    }

    [Fact]
    public void Private_method_on_this_is_invoked_reflectively()
    {
      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => GetTuple().a == GetTuple().b));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("GetTuple().a: \"b\"", expected);
      Assert.Equal("GetTuple().a: \"a\"", actual);
    }

    [Fact]
    public void ReferenceEquals_is_intercepted()
    {
      var instance1 = new object();
      var instance2 = new object();

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => ReferenceEquals(instance1, instance2)));

      Assert.True(wasIntercepted);

      var (expected, actual) = Decomposition(exception!);
      Assert.Equal("instance1 and instance2 should be the same instance.", expected);
      Assert.Equal("instance1: System.Object\ninstance2: System.Object", actual);
    }

    [Fact]
    public void Negated_ReferenceEquals_is_intercepted_without_actual()
    {
      var instance1 = new object();
      var instance2 = instance1;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => !ReferenceEquals(instance1, instance2)));

      Assert.True(wasIntercepted);

      var (expected, _) = Decomposition(exception!);
      Assert.Equal("instance1 and instance2 should be different instances.", expected);
      Assert.Empty((string[])exception!.Data["Assertive.Actual"]!);
    }

    private (string a, string b) GetTuple()
    {
      return ("a", "b");
    }

    [Fact]
    public void Exception_cause_is_attributed_on_private_types()
    {
      Secret secret = null!;

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => secret.Value == 4));

      Assert.True(wasIntercepted);
      Assert.NotNull(exception);

      var handled = (string[])exception!.Data["Assertive.HandledExceptions"]!;
      Assert.Equal("NullReferenceException caused by accessing Value on secret which was null.", StripAnsi(handled.Single()));
    }

    [Fact]
    public void Exception_cause_inside_lambda_gets_item_context()
    {
      var users = new[] { new User { Name = "a" }, new User { Name = null! } };

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => users.All(u => u.Name.Length > 0)));

      Assert.True(wasIntercepted);
      Assert.NotNull(exception);

      var handled = StripAnsi(((string[])exception!.Data["Assertive.HandledExceptions"]!).Single());
      Assert.StartsWith("NullReferenceException caused by accessing Length on u.Name which was null.", handled);
      Assert.Contains("On item [1] of users:", handled);
    }

    private enum Mood
    {
      Happy,
      Grumpy,
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
