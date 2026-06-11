namespace Assertive.Test.Aot
{
  using System;
  using System.Linq;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using static Assertive.DSL;
  using AssertiveAssert = Assertive.Assert;

  /// <summary>
  /// Native AOT smoke test: a plain console app (test frameworks don't run under AOT)
  /// published with PublishAot=true. Each check exercises one of the reflective paths in
  /// GeneratedAssert (closure capture, captured this, private-type member access, reflective
  /// method invocation, enum-constant resolution, exception attribution) plus the degraded
  /// and Throws paths. Exit code is the number of failed checks.
  ///
  /// Two publish modes are pinned:
  /// - default (TrimmerRootAssembly roots the app): full decomposition works;
  /// - -p:RootApp=false + the --expect-degraded flag: closure metadata is trimmed away,
  ///   so every failing assertion must still degrade to a source-text report instead of
  ///   leaking an infrastructure exception, and passing assertions must stay passing.
  /// </summary>
  public static class Program
  {
    private static int _failed;
    private static bool _expectDegraded;

    public static int Main(string[] args)
    {
      _expectDegraded = args.Contains("--expect-degraded");

      // Deterministic plain-text output regardless of TTY detection.
      Config.Configuration.Colors.Enabled = false;

      Console.WriteLine(_expectDegraded
        ? "Mode: fully trimmed (expecting graceful source-text degradation)"
        : "Mode: rooted (expecting full decomposition)");
      Console.WriteLine();

      Check(nameof(Equality_is_decomposed_from_the_closure), Equality_is_decomposed_from_the_closure);
      Check(nameof(InstanceChecks.Captured_this_is_resolved), () => new InstanceChecks().Captured_this_is_resolved());
      Check(nameof(InstanceChecks.Private_method_on_this_is_invoked_reflectively), () => new InstanceChecks().Private_method_on_this_is_invoked_reflectively());
      Check(nameof(Private_nested_operand_type_is_read_reflectively), Private_nested_operand_type_is_read_reflectively);
      Check(nameof(Private_nested_enum_constant_is_resolved_reflectively), Private_nested_enum_constant_is_resolved_reflectively);
      Check(nameof(Null_comparison_uses_the_null_pattern), Null_comparison_uses_the_null_pattern);
      Check(nameof(ReferenceEquals_is_decomposed), ReferenceEquals_is_decomposed);
      Check(nameof(Exception_cause_is_attributed), Exception_cause_is_attributed);
      Check(nameof(Exception_cause_inside_lambda_gets_item_context), Exception_cause_inside_lambda_gets_item_context);
      Check(nameof(Logical_and_is_split), Logical_and_is_split);
      Check(nameof(Locals_are_serialized), Locals_are_serialized);
      Check(nameof(Degraded_path_reports_unintercepted_marker), Degraded_path_reports_unintercepted_marker);
      Check(nameof(Passing_assertion_does_not_throw), Passing_assertion_does_not_throw);
      Check(nameof(Async_equality_is_decomposed_typed), () => Async_equality_is_decomposed_typed().GetAwaiter().GetResult());
      Check(nameof(Async_equality_is_decomposed_reflectively), () => Async_equality_is_decomposed_reflectively().GetAwaiter().GetResult());
      Check(nameof(Async_degraded_path_reports_unintercepted_marker), () => Async_degraded_path_reports_unintercepted_marker().GetAwaiter().GetResult());
      Check(nameof(Passing_async_assertion_does_not_throw), () => Passing_async_assertion_does_not_throw().GetAwaiter().GetResult());
      Check(nameof(Throws_returns_the_exception), Throws_returns_the_exception);
      Check(nameof(Throws_fails_when_nothing_is_thrown), Throws_fails_when_nothing_is_thrown);
      Check(nameof(Throws_with_passing_predicate_does_not_throw), Throws_with_passing_predicate_does_not_throw);
      Check(nameof(Throws_with_failing_predicate_reports_the_predicate), Throws_with_failing_predicate_reports_the_predicate);

      Console.WriteLine();
      Console.WriteLine(_failed == 0 ? "All checks passed." : $"{_failed} check(s) FAILED.");

      return _failed;
    }

    private static void Equality_is_decomposed_from_the_closure()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var exception = Capture(() => Assert(() => x.IndexOf('b') == expectedIndex));

      ExpectFailure(exception, "x.IndexOf('b') == expectedIndex", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("x.IndexOf('b'): 5", expected);
        ExpectEqual("x.IndexOf('b'): 3", actual);
      });
    }

    private static void Private_nested_operand_type_is_read_reflectively()
    {
      var secret = new Secret();

      var exception = Capture(() => Assert(() => secret.Value == 4));

      ExpectFailure(exception, "secret.Value == 4", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("secret.Value: 4", expected);
        ExpectEqual("secret.Value: 3", actual);
      });
    }

    private static void Private_nested_enum_constant_is_resolved_reflectively()
    {
      Mood? a = null;

      var exception = Capture(() => Assert(() => a == Mood.Grumpy));

      ExpectFailure(exception, "a == Mood.Grumpy", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("a: Mood.Grumpy", expected);
        ExpectEqual("a: null", actual);
      });
    }

    private static void Null_comparison_uses_the_null_pattern()
    {
      string? value = "not null";

      var exception = Capture(() => Assert(() => value == null));

      ExpectFailure(exception, "value == null", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("value should be null.", expected);
        ExpectEqual("\"not null\"", actual);
      });
    }

    private static void ReferenceEquals_is_decomposed()
    {
      var instance1 = new object();
      var instance2 = new object();

      var exception = Capture(() => Assert(() => ReferenceEquals(instance1, instance2)));

      ExpectFailure(exception, "ReferenceEquals(instance1, instance2)", () =>
      {
        var (expected, _) = Decomposition(exception!);
        ExpectEqual("instance1 and instance2 should be the same instance.", expected);
      });
    }

    private static void Exception_cause_is_attributed()
    {
      Secret secret = null!;

      var exception = Capture(() => Assert(() => secret.Value == 4));

      // The evaluation exception must be reported in both modes.
      Expect(exception != null, "assertion should fail");
      Expect(StripAnsi(exception!.Message).Contains("NullReferenceException"),
        $"missing exception report: {exception.Message}");

      ExpectFailure(exception, "secret.Value == 4", () =>
      {
        var handled = (string[])exception.Data["Assertive.HandledExceptions"]!;
        ExpectEqual("NullReferenceException caused by accessing Value on secret which was null.", StripAnsi(handled.Single()));
      });
    }

    private static void Exception_cause_inside_lambda_gets_item_context()
    {
      var users = new[] { new User { Name = "a" }, new User { Name = null! } };

      var exception = Capture(() => Assert(() => users.All(u => u.Name.Length > 0)));

      Expect(exception != null, "assertion should fail");
      Expect(StripAnsi(exception!.Message).Contains("NullReferenceException"),
        $"missing exception report: {exception.Message}");

      ExpectFailure(exception, "users.All(u => u.Name.Length > 0)", () =>
      {
        var handled = StripAnsi(((string[])exception.Data["Assertive.HandledExceptions"]!).Single());
        Expect(handled.StartsWith("NullReferenceException caused by accessing Length on u.Name which was null."),
          $"unexpected cause: {handled}");
        Expect(handled.Contains("On item [1] of users:"), $"missing item context: {handled}");
      });
    }

    private static void Logical_and_is_split()
    {
      var value = "ab";

      var exception = Capture(() => Assert(() => value.Contains('a') && value.Contains('z')));

      ExpectFailure(exception, "value.Contains('a') && value.Contains('z')", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("value should contain the substring 'z'.", expected);
        ExpectEqual("value: \"ab\"", actual);
      });
    }

    private static void Locals_are_serialized()
    {
      var x = "foobar";
      var expectedIndex = 5;

      var exception = Capture(() => Assert(() => x.IndexOf('b') == expectedIndex));

      ExpectFailure(exception, "x.IndexOf('b') == expectedIndex", () =>
      {
        var message = StripAnsi(exception!.Message);
        Expect(message.Contains("LOCALS"), $"missing LOCALS section: {message}");
        Expect(message.Contains("x:"), $"missing local x: {message}");
      });
    }

    private static void Degraded_path_reports_unintercepted_marker()
    {
      var x = "foobar";
      Func<bool> storedCondition = () => x.Length == 5;

      var exception = Capture(() => Assert(storedCondition));

      // Not intercepted (no lambda literal): without CallerArgumentExpression on the public
      // API there is no source text; the failure says so explicitly.
      Expect(exception != null, "assertion should fail");
      Expect(StripAnsi(exception!.Message).Contains("was not intercepted"), $"missing unintercepted marker: {exception.Message}");
    }

    private static void Passing_assertion_does_not_throw()
    {
      var x = "foobar";

      var exception = Capture(() => Assert(() => x.IndexOf('o') == 1));

      Expect(exception == null, $"passing assertion threw: {exception?.Message}");
    }

    private static void Throws_returns_the_exception()
    {
      var thrown = AssertiveAssert.Throws(() => ThrowSomething());

      Expect(thrown is InvalidOperationException, $"unexpected exception: {thrown}");
    }

    private static void Throws_fails_when_nothing_is_thrown()
    {
      var exception = Capture(() => AssertiveAssert.Throws(() => DoNothing()));

      Expect(exception != null, "Throws should fail when nothing is thrown");
      Expect(StripAnsi(exception!.Message).Contains("DoNothing()"), $"missing action source: {exception.Message}");
    }

    private static void Throws_with_passing_predicate_does_not_throw()
    {
      // The predicate's captured local must not be read on the passing path: before the
      // best-effort reporting fix, a fully-trimmed publish leaked an infrastructure
      // exception here before the action even ran.
      var expectedMessage = "boom";

      var thrown = AssertiveAssert.Throws(() => ThrowSomething(), e => e.Message == expectedMessage);

      Expect(thrown is InvalidOperationException, $"unexpected exception: {thrown}");
    }

    private static void Throws_with_failing_predicate_reports_the_predicate()
    {
      var expectedMessage = "not boom";

      var exception = Capture(() => AssertiveAssert.Throws(() => ThrowSomething(), e => e.Message == expectedMessage));

      ExpectFailure(exception, "e.Message == expectedMessage", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("e.Message: \"not boom\"", expected);
        ExpectEqual("e.Message: \"boom\"", actual);
      });
    }

    private static async Task Async_equality_is_decomposed_typed()
    {
      var fetcher = new AsyncFetcher();
      var expectedValue = 5;

      // Member access on a captured local: the awaited operand pastes typed into the
      // async render callback, no reflective await involved.
      var exception = await CaptureAsync(() => Assert(async () => await fetcher.GetAsync(3) == expectedValue));

      ExpectFailure(exception, "await fetcher.GetAsync(3) == expectedValue", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("await fetcher.GetAsync(3): 5", expected);
        ExpectEqual("await fetcher.GetAsync(3): 3", actual);
      });
    }

    private static async Task Async_equality_is_decomposed_reflectively()
    {
      var expectedValue = 5;

      // A simple-name static call fails the typed strategy, so the operand re-evaluates
      // reflectively; the await itself stays a typed cast (Task<int> is nameable), which
      // keeps it AOT-safe — Task<T>.Result metadata is trimmed even in rooted publishes.
      var exception = await CaptureAsync(() => Assert(async () => await ValueAsync(3) == expectedValue));

      ExpectFailure(exception, "await ValueAsync(3) == expectedValue", () =>
      {
        var (expected, actual) = Decomposition(exception!);
        ExpectEqual("await ValueAsync(3): 5", expected);
        ExpectEqual("await ValueAsync(3): 3", actual);
      });
    }

    private static async Task Async_degraded_path_reports_unintercepted_marker()
    {
      Func<Task<bool>> stored = () => Task.FromResult(false);

      var exception = await CaptureAsync(() => Assert(stored));

      Expect(exception != null, "assertion should fail");
      Expect(StripAnsi(exception!.Message).Contains("was not intercepted"), $"missing unintercepted marker: {exception.Message}");
    }

    private static async Task Passing_async_assertion_does_not_throw()
    {
      var fetcher = new AsyncFetcher();

      var exception = await CaptureAsync(() => Assert(async () => await fetcher.GetAsync(1) == 1));

      Expect(exception == null, $"passing async assertion threw: {exception?.Message}");
    }

    internal static Task<int> ValueAsync(int value) => Task.FromResult(value);

    public sealed class AsyncFetcher
    {
      public Task<int> GetAsync(int value) => Task.FromResult(value);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> assertion)
    {
      try
      {
        await assertion();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    private static void ThrowSomething() => throw new InvalidOperationException("boom");

    private static void DoNothing() { }

    // -------- harness --------

    private static void Check(string name, Action check)
    {
      try
      {
        check();
        Console.WriteLine($"PASS {name}");
      }
      catch (Exception ex)
      {
        _failed++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine($"     {ex.Message.Replace("\n", "\n     ")}");
      }
    }

    /// <summary>
    /// A failing assertion must throw in both modes. Rooted: the decomposed expectations
    /// hold. Fully trimmed: reporting degrades, but the failure must still be an Assertive
    /// failure carrying the assertion's source text — never a leaked infrastructure
    /// exception like "could not locate captured variable".
    /// </summary>
    private static void ExpectFailure(Exception? exception, string sourceText, Action verifyDecomposed)
    {
      Expect(exception != null, "assertion should fail");

      if (_expectDegraded)
      {
        var message = StripAnsi(exception!.Message);
        Expect(message.Contains(sourceText), $"missing source text: {message}");
        Expect(exception.Data.Contains("Assertive.Expected"), $"not an Assertive failure: {message}");
        return;
      }

      verifyDecomposed();
    }

    private static Exception? Capture(Action assertion)
    {
      try
      {
        assertion();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    private static (string Expected, string Actual) Decomposition(Exception exception)
    {
      return (
        StripAnsi(string.Join("\n", (string[])exception.Data["Assertive.Expected"]!)),
        StripAnsi(string.Join("\n", (string[])exception.Data["Assertive.Actual"]!)));
    }

    private static string StripAnsi(string input) => Regex.Replace(input, ((char)27) + @"\[[0-9;]*[A-Za-z]", "");

    private static void Expect(bool condition, string detail)
    {
      if (!condition)
      {
        throw new Exception(detail);
      }
    }

    private static void ExpectEqual(string expected, string actual)
    {
      if (expected != actual)
      {
        throw new Exception($"expected: {expected}\nactual:   {actual}");
      }
    }

    private sealed class InstanceChecks
    {
      private readonly int _value = 41;

      public void Captured_this_is_resolved()
      {
        var exception = Capture(() => Assert(() => _value == 42));

        ExpectFailure(exception, "_value == 42", () =>
        {
          var (expected, actual) = Decomposition(exception!);
          ExpectEqual("_value: 42", expected);
          ExpectEqual("_value: 41", actual);
        });
      }

      public void Private_method_on_this_is_invoked_reflectively()
      {
        var exception = Capture(() => Assert(() => GetTuple().a == GetTuple().b));

        ExpectFailure(exception, "GetTuple().a == GetTuple().b", () =>
        {
          if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
          {
            // Known AOT limitation even when rooted: tuple element access goes through
            // GetMemberValue on ValueTuple`2, whose reflection metadata lives in CoreLib
            // and is not rooted by TrimmerRootAssembly. The decomposition fails and
            // degrades to a source-text report instead of leaking.
            Expect(StripAnsi(exception!.Message).Contains("GetTuple().a == GetTuple().b"),
              $"missing source text: {exception.Message}");
            return;
          }

          var (expected, actual) = Decomposition(exception!);
          ExpectEqual("GetTuple().a: \"b\"", expected);
          ExpectEqual("GetTuple().a: \"a\"", actual);
        });
      }

      private (string a, string b) GetTuple()
      {
        return ("a", "b");
      }
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

  internal sealed class User
  {
    public string Name = null!;
  }
}
