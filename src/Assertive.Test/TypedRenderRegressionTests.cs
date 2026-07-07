using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Regression tests for source-generator bugs uncovered when upgrading EVA.TestSuite
  /// to the interceptor-based Assertive. Each test exercises an assertion pattern that
  /// previously produced uncompilable generated code (the test project itself would not
  /// build). The patterns mirror real EVA assertion shapes.
  /// </summary>
  public class TypedRenderRegressionTests : AssertionTestBase
  {
    // --------------------------------------------------------------------------------
    // Bug: `global::` inside interpolated-string interpolation holes (CS0103)
    // --------------------------------------------------------------------------------
    // The TypedRenderRewriter fully-qualified type/member names with a `global::` prefix,
    // but C# rejects `global::` inside a `$"...{global::Foo.Bar}..."` interpolation hole.
    // The fix uses a non-global format for names emitted inside an InterpolationSyntax.
    //
    // Regression: without the fix, the generated `var __v1 = ($"<{global::...}>")` fails
    // to compile with "The name 'global' does not exist in the current context".

    [Fact]
    public void Interpolated_string_referencing_external_type_fails_cleanly()
    {
      // The interpolation hole {RegressionConstants.Username} forces the rewriter to
      // qualify RegressionConstants from the generated namespace; the `global::` prefix
      // it would normally emit is illegal inside an interpolation hole.
      ShouldFail(() => $"user<{RegressionConstants.Username}>" == "user<root>");
    }

    [Fact]
    public void Interpolated_string_referencing_external_type_passes()
    {
      Assert.That(() => $"user<{RegressionConstants.Username}>" == "user<admin>");
    }

    // --------------------------------------------------------------------------------
    // Bug: Target-typed `new(...)` loses its inferred type in generated code (CS1729)
    // --------------------------------------------------------------------------------
    // `new(2100, 3, 2)` is target-typed to DateTime only in its original context (a
    // method parameter). The ExceptionStepCollector's argument evaluator wraps it as
    // `(object)(new(2100, 3, 2))`, which re-targets the `new` to `object` -> CS1729.
    // The fix materializes the inferred type explicitly (`new global::System.DateTime(...)`).

    [Fact]
    public void Target_typed_new_as_method_argument_fails_cleanly()
    {
      var dict = new Dictionary<DateTime, string> { [new DateTime(2000, 1, 1)] = "present" };

      // The argument `new(2100, 3, 2)` is target-typed to DateTime (the key type);
      // without the fix the generated Args evaluator `(object)(new(2100, 3, 2))` fails.
      ShouldFail(() => dict.ContainsKey(new(2100, 3, 2)));
    }

    [Fact]
    public void Target_typed_new_as_method_argument_passes()
    {
      var dict = new Dictionary<DateTime, string> { [new DateTime(2100, 3, 2)] = "present" };

      Assert.That(() => dict.ContainsKey(new(2100, 3, 2)));
    }

    // --------------------------------------------------------------------------------
    // Bug: `nameof(Type.InstanceMember)` walked as a runtime expression (CS0119)
    // --------------------------------------------------------------------------------
    // The ExceptionStepCollector descended into nameof arguments and tried to evaluate
    // the type name as a runtime value (`(object)(global::System.DateTime)`). The fix
    // short-circuits nameof invocations in VisitInvocation: nameof arguments are
    // compile-time constants, not runtime expressions.

    [Fact]
    public void Nameof_with_instance_member_fails_cleanly()
    {
      // nameof(DateTime.Now) references an instance property via the type name; without
      // the fix the collector evaluates `DateTime` as a value -> CS0119.
      ShouldFail(() => "NotNow" == nameof(DateTime.Now));
    }

    [Fact]
    public void Nameof_with_instance_member_passes()
    {
      Assert.That(() => "Now" == nameof(DateTime.Now));
    }

    // --------------------------------------------------------------------------------
    // Bug: Method group argument boxed as object (CS0030)
    // --------------------------------------------------------------------------------
    // A method group passed as a delegate (e.g. `.All(list.Contains)`) was wrapped in
    // `(object)(...)` in the generated Args evaluators, which is invalid for an
    // unconverted method group. The fix bails out of Compile for method groups so the
    // call site degrades to "null" instead of emitting broken code.

    [Fact]
    public void Method_group_argument_fails_cleanly()
    {
      var haystack = new List<int> { 1, 2, 3 };
      var needles = new[] { 1, 2, 4 };

      // .All(haystack.Contains) passes a method group as the predicate; without the fix
      // the generated `(object)(haystack.Contains)` fails with CS0030.
      ShouldFail(() => needles.All(haystack.Contains));
    }

    [Fact]
    public void Method_group_argument_passes()
    {
      var haystack = new List<int> { 1, 2, 3 };
      var needles = new[] { 1, 2, 3 };

      Assert.That(() => needles.All(haystack.Contains));
    }

    // --------------------------------------------------------------------------------
    // Bug: Target-typed conditional with null (CS0173)
    // --------------------------------------------------------------------------------
    // `cond ? dateTimeValue : null` resolves to `DateTime?` only when the surrounding
    // context supplies the target type; pasted into a `var` or `(object)` cast it loses
    // that context. The fix casts the value branch to `T?` explicitly.

    [Fact]
    public void Nullable_conditional_with_null_fails_cleanly()
    {
      var date = new DateTime(2000, 1, 1);
      DateTime? nullable = new DateTime(2000, 1, 2);

      // The conditional `true ? date : null` is target-typed to DateTime?; without the
      // fix, `var __v2 = (true ? date : null)` fails with CS0173.
      ShouldFail(() => nullable == (true ? date : null));
    }

    [Fact]
    public void Nullable_conditional_with_null_passes()
    {
      var date = new DateTime(2000, 1, 1);
      DateTime? nullable = new DateTime(2000, 1, 1);

      Assert.That(() => nullable == (true ? date : null));
    }
  }

  /// <summary>
  /// Public type referenced from assertion interpolation holes so the TypedRenderRewriter
  /// must fully-qualify it from the generated namespace (regression for the `global::`
  /// in interpolation hole bug).
  /// </summary>
  public static class RegressionConstants
  {
    public const string Username = "admin";
  }
}
