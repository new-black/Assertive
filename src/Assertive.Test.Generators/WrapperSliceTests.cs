namespace Assertive.Test.Generators
{
  using System;
  using System.Linq.Expressions;
  using Assertive.Runtime;
  using Xunit;
  using Assert = Xunit.Assert;
  using AssertiveAssert = Assertive.Assert;

  /// <summary>
  /// Tests for [AssertionWrapper] interception: wrapper call sites with whitelisted lambda
  /// literals are routed to the AssertionHandle overload with a generated evaluator; all
  /// other call sites run the sugar overload's degraded path with identical behavior.
  /// </summary>
  public class WrapperSliceTests
  {
    // Static wrapper pair.
    [AssertionWrapper]
    internal static Exception? CaptureFailure(Expression<Func<bool>> assertion, string label)
      => CaptureFailure(AssertionHandle.Degraded(assertion), label);

    internal static Exception? CaptureFailure(AssertionHandle assertion, string label)
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
    internal Exception? CaptureFailureInstance(Expression<Func<bool>> assertion)
      => CaptureFailureInstance(AssertionHandle.Degraded(assertion));

    internal Exception? CaptureFailureInstance(AssertionHandle assertion)
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
    public void Static_wrapper_is_intercepted_and_matches_degraded_path()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var before = GeneratedAssert.InterceptedCallCount;
      var intercepted = CaptureFailure(() => x.IndexOf('b') == expectedIndex, "label");
      Assert.True(GeneratedAssert.InterceptedCallCount > before);

      // Passing the expression as a variable can never be intercepted: the degraded baseline.
      Expression<Func<bool>> baselineExpression = () => x.IndexOf('b') == expectedIndex;
      var baseline = CaptureFailure(baselineExpression, "label");

      Assert.NotNull(intercepted);
      Assert.NotNull(baseline);
      Assert.Equal(baseline!.GetType(), intercepted!.GetType());
      Assert.Equal(baseline.Message, intercepted.Message);
    }

    [Fact]
    public void Instance_wrapper_is_intercepted_and_matches_degraded_path()
    {
      var value = 41;

      var before = GeneratedAssert.InterceptedCallCount;
      var intercepted = CaptureFailureInstance(() => value == 42);
      Assert.True(GeneratedAssert.InterceptedCallCount > before);

      Expression<Func<bool>> baselineExpression = () => value == 42;
      var baseline = CaptureFailureInstance(baselineExpression);

      Assert.NotNull(intercepted);
      Assert.Equal(baseline!.Message, intercepted!.Message);
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
    public void Wrapper_with_non_whitelisted_body_is_not_intercepted_but_still_works()
    {
      string? x = "not null";

      var before = GeneratedAssert.InterceptedCallCount;
      var result = CaptureFailure(() => x == null, "label");

      Assert.Equal(before, GeneratedAssert.InterceptedCallCount);
      Assert.NotNull(result);
    }

    [Fact]
    public void Exception_during_wrapper_evaluation_matches_degraded_path()
    {
      var user = new User();

      var before = GeneratedAssert.InterceptedCallCount;
      var intercepted = CaptureFailure(() => user.Name.ToUpper() == "FOO", "label");
      Assert.True(GeneratedAssert.InterceptedCallCount > before);

      Expression<Func<bool>> baselineExpression = () => user.Name.ToUpper() == "FOO";
      var baseline = CaptureFailure(baselineExpression, "label");

      Assert.NotNull(intercepted);
      Assert.Equal(baseline!.GetType(), intercepted!.GetType());
      Assert.Equal(baseline.Message, intercepted.Message);
    }
  }
}
