namespace Assertive.Test.Generators
{
  using System;
  using System.Runtime.CompilerServices;
  using System.Text.RegularExpressions;
  using Assertive.Runtime;
  using Xunit;
  using Assert = Xunit.Assert;
  using AssertiveAssert = Assertive.Assert;

  /// <summary>
  /// Tests for [AssertionWrapper] interception: wrapper call sites with whitelisted lambda
  /// literals are routed to the AssertionHandle overload with a generated assertion; all
  /// other call sites run the sugar overload's degraded path.
  /// </summary>
  public class WrapperSliceTests
  {
    private static string StripAnsi(string input) => Regex.Replace(input, @"\[[0-9;]*[A-Za-z]", "");

    // Static wrapper pair.
    [AssertionWrapper]
    internal static Exception? CaptureFailure(Func<bool> assertion, string label,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "")
      => CaptureFailure(AssertionHandle.Degraded(assertion, assertionExpression), label, assertionExpression);

    internal static Exception? CaptureFailure(AssertionHandle assertion, string label, string assertionExpression = "")
    {
      try
      {
        AssertiveAssert.That(assertion);
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    // Instance wrapper pair (intercepted via an extension-method interceptor).
    [AssertionWrapper]
    internal Exception? CaptureFailureInstance(Func<bool> assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "")
      => CaptureFailureInstance(AssertionHandle.Degraded(assertion, assertionExpression), assertionExpression);

    internal Exception? CaptureFailureInstance(AssertionHandle assertion, string assertionExpression = "")
    {
      try
      {
        AssertiveAssert.That(assertion);
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    [Fact]
    public void Static_wrapper_is_intercepted_with_decomposed_values()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var before = GeneratedAssert.InterceptedCallCount;
      var exception = CaptureFailure(() => x.IndexOf('b') == expectedIndex, "label");

      Assert.True(GeneratedAssert.InterceptedCallCount > before);
      Assert.NotNull(exception);

      var expected = StripAnsi(string.Join("\n", (string[])exception!.Data["Assertive.Expected"]!));
      Assert.Equal("x.IndexOf('b'): 5", expected);
    }

    [Fact]
    public void Instance_wrapper_is_intercepted_with_decomposed_values()
    {
      var value = 41;

      var before = GeneratedAssert.InterceptedCallCount;
      var exception = CaptureFailureInstance(() => value == 42);

      Assert.True(GeneratedAssert.InterceptedCallCount > before);

      var expected = StripAnsi(string.Join("\n", (string[])exception!.Data["Assertive.Expected"]!));
      Assert.Equal("value: 42", expected);
    }

    [Fact]
    public void Passing_assertion_through_wrapper_is_intercepted_and_returns_no_failure()
    {
      var x = "foobar";

      var before = GeneratedAssert.InterceptedCallCount;
      var result = CaptureFailure(() => x.IndexOf('o') == 1, "label");

      Assert.True(GeneratedAssert.InterceptedCallCount > before);
      Assert.Null(result);
    }

    [Fact]
    public void Degraded_wrapper_path_reports_source_text()
    {
      var x = "foobar";
      Func<bool> storedCondition = () => x.Length == 5;

      var before = GeneratedAssert.InterceptedCallCount;
      var exception = CaptureFailure(storedCondition, "label");

      Assert.Equal(before, GeneratedAssert.InterceptedCallCount);
      Assert.NotNull(exception);
      Assert.Contains("storedCondition", StripAnsi(exception!.Message));
      Assert.Empty((string[])exception.Data["Assertive.Expected"]!);
    }

    [Fact]
    public void Wrapper_with_non_whitelisted_body_is_not_intercepted_but_still_works()
    {
      var x = "not null";

      var before = GeneratedAssert.InterceptedCallCount;
      var exception = CaptureFailure(() => x.Contains('a') && x.Contains('z'), "label");

      Assert.Equal(before, GeneratedAssert.InterceptedCallCount);
      Assert.NotNull(exception);
      Assert.Contains("x.Contains('a') && x.Contains('z')", StripAnsi(exception!.Message));
    }
  }
}
