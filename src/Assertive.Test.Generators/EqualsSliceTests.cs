namespace Assertive.Test.Generators
{
  using System;
  using System.Linq.Expressions;
  using Assertive.Runtime;
  using Xunit;
  using Assert = Xunit.Assert;
  using AssertiveAssert = Assertive.Assert;

  /// <summary>
  /// Differential tests for the Phase 0.5 equality slice: call sites the generator
  /// intercepts must behave identically to the standard pipeline, and call sites outside
  /// the whitelist must not be intercepted at all.
  /// </summary>
  public class EqualsSliceTests
  {
    private static Exception? Catch(Action action)
    {
      try
      {
        action();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    /// <summary>
    /// The baseline non-generated pipeline: the argument here is a variable, not a lambda
    /// literal, so this call site can never be intercepted.
    /// </summary>
    private static Exception? Baseline(Expression<Func<bool>> assertion, object? message = null)
    {
      return Catch(() =>
      {
        if (message != null)
        {
          AssertiveAssert.That(assertion, message);
        }
        else
        {
          AssertiveAssert.That(assertion);
        }
      });
    }

    private static (Exception? Exception, bool WasIntercepted) Run(Action assert)
    {
      var before = GeneratedAssert.InterceptedCallCount;
      var exception = Catch(assert);

      return (exception, GeneratedAssert.InterceptedCallCount > before);
    }

    [Fact]
    public void Failing_equality_is_intercepted_and_matches_baseline()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => x.IndexOf('b') == expectedIndex));
      var baseline = Baseline(() => x.IndexOf('b') == expectedIndex);

      Assert.True(wasIntercepted);
      Assert.NotNull(intercepted);
      Assert.NotNull(baseline);
      Assert.Equal(baseline!.GetType(), intercepted!.GetType());
      Assert.Equal(baseline.Message, intercepted.Message);
    }

    [Fact]
    public void Passing_equality_is_intercepted_and_does_not_throw()
    {
      var x = "foobar";

      var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => x.IndexOf('o') == 1));

      Assert.True(wasIntercepted);
      Assert.Null(intercepted);
    }

    [Fact]
    public void Failing_not_equals_matches_baseline()
    {
      var left = "same";
      var right = "same";

      var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => left != right));
      var baseline = Baseline(() => left != right);

      Assert.True(wasIntercepted);
      Assert.NotNull(intercepted);
      Assert.Equal(baseline!.Message, intercepted!.Message);
    }

    [Fact]
    public void Custom_message_overload_matches_baseline()
    {
      var actual = 41;
      var expected = 42;

      var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => actual == expected, "the answer matters"));
      var baseline = Baseline(() => actual == expected, "the answer matters");

      Assert.True(wasIntercepted);
      Assert.Equal(baseline!.Message, intercepted!.Message);
    }

    [Fact]
    public void Enum_comparison_with_type_qualifier_matches_baseline()
    {
      var comparison = StringComparison.OrdinalIgnoreCase;

      var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => comparison == StringComparison.Ordinal));
      var baseline = Baseline(() => comparison == StringComparison.Ordinal);

      Assert.True(wasIntercepted);
      Assert.Equal(baseline!.Message, intercepted!.Message);
    }

    [Fact]
    public void Exception_during_evaluation_matches_baseline()
    {
      var user = new User();

      var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => user.Name.ToUpper() == "FOO"));
      var baseline = Baseline(() => user.Name.ToUpper() == "FOO");

      Assert.True(wasIntercepted);
      Assert.NotNull(intercepted);
      Assert.Equal(baseline!.GetType(), intercepted!.GetType());
      Assert.Equal(baseline.Message, intercepted.Message);
    }

    [Fact]
    public void Captured_lambda_parameter_from_enclosing_scope_matches_baseline()
    {
      foreach (var i in new[] { 1, 2, 3 })
      {
        var (intercepted, wasIntercepted) = Run(() => AssertiveAssert.That(() => i == 2));
        var baseline = Baseline(() => i == 2);

        Assert.True(wasIntercepted);

        if (i == 2)
        {
          Assert.Null(intercepted);
          Assert.Null(baseline);
        }
        else
        {
          Assert.Equal(baseline!.Message, intercepted!.Message);
        }
      }
    }

    [Fact]
    public void Null_comparison_is_not_intercepted_but_still_works()
    {
      string? value = "not null";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => value == null));

      Assert.False(wasIntercepted);
      Assert.NotNull(exception);
    }

    [Fact]
    public void Length_comparison_is_not_intercepted_but_still_works()
    {
      var x = "foobar";

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => x.Length == 5));

      Assert.False(wasIntercepted);
      Assert.NotNull(exception);
    }

    [Fact]
    public void Instance_field_access_is_not_intercepted_but_still_works()
    {
      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => _instanceField == 4));

      Assert.False(wasIntercepted);
      Assert.NotNull(exception);
    }

    private readonly int _instanceField = 3;

    [Fact]
    public void Private_nested_operand_type_is_not_intercepted_but_still_works()
    {
      var secret = new Secret();

      var (exception, wasIntercepted) = Run(() => AssertiveAssert.That(() => secret.Value == 4));

      Assert.False(wasIntercepted);
      Assert.NotNull(exception);
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
