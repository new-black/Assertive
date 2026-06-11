using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Async assertions: the That/Assert overloads taking Func&lt;Task&lt;bool&gt;&gt; (no Async
  /// suffix — an async lambda only converts to the Task-returning overload, so the names
  /// can be shared). Intercepted call sites get an async interceptor whose render callback
  /// re-awaits the operands, so decomposition matches the synchronous patterns.
  /// </summary>
  public class AsyncAssertionTests : AssertionTestBase
  {
    // Internal rather than private: generated reporting re-evaluates the awaited calls,
    // and reflective re-invocation of static methods requires assembly-level access (the
    // same rule the synchronous reflective strategy applies).
    internal static Task<int> ValueAsync(int value) => Task.FromResult(value);

    internal static Task<string?> NullStringAsync() => Task.FromResult<string?>(null);

    internal static Task<List<int>> ListAsync() => Task.FromResult(new List<int> { 1, 2, 3 });

    private static async Task<int> ThrowingAsync()
    {
      await Task.Yield();
      throw new InvalidOperationException("boom");
    }

    private async Task ShouldFailAsync(Func<Task> assertion, string expectedMessage, string actualMessage)
    {
      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(assertion);

      var expected = StripAnsi(string.Join(Environment.NewLine, ex.Data["Assertive.Expected"] as string[] ?? []));
      var actual = StripAnsi(string.Join(Environment.NewLine, ex.Data["Assertive.Actual"] as string[] ?? []));

      Assert(() => expected == expectedMessage && actual == actualMessage);
    }

    [Fact]
    public async Task Passing_async_assertion_passes()
    {
      await Assert(async () => await ValueAsync(1) == 1);
      await Assertive.Assert.That(async () => await ValueAsync(1) == 1);
    }

    [Fact]
    public async Task Failing_equality_decomposes_awaited_operands()
    {
      var expected = 5;

      await ShouldFailAsync(() => Assert(async () => await ValueAsync(10) == expected),
        "await ValueAsync(10): 5", "await ValueAsync(10): 10");
    }

    [Fact]
    public async Task Assert_That_overload_is_also_intercepted()
    {
      var expected = 5;

      await ShouldFailAsync(() => Assertive.Assert.That(async () => await ValueAsync(10) == expected),
        "await ValueAsync(10): 5", "await ValueAsync(10): 10");
    }

    [Fact]
    public async Task Null_check_on_awaited_operand()
    {
      await ShouldFailAsync(() => Assert(async () => await NullStringAsync() != null),
        "await NullStringAsync() should not be null.", "null");
    }

    [Fact]
    public async Task And_chain_reports_the_failing_conjunct()
    {
      var a = 1;

      await ShouldFailAsync(() => Assert(async () => a == 1 && await ValueAsync(7) == 2),
        "await ValueAsync(7): 2", "await ValueAsync(7): 7");
    }

    [Fact]
    public async Task Count_comparison_on_awaited_collection()
    {
      await ShouldFailAsync(() => Assert(async () => (await ListAsync()).Count == 2),
        "(await ListAsync()) should have a Count equal to 2.", "Count: 3.");
    }

    [Fact]
    public async Task Captured_locals_are_reported()
    {
      var threshold = 5;

      // `threshold + 2` rather than a whole-operand local: those display as the operand
      // value instead of under LOCALS (same rule as synchronous assertions).
      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert(async () => await ValueAsync(1) > threshold + 2));

      Xunit.Assert.Contains("threshold: 5", StripAnsi(ex.Message));
    }

    [Fact]
    public async Task Message_and_context_are_reported()
    {
      var orderID = 10;

      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert(async () => await ValueAsync(1) == 2, "order mismatch", () => orderID));

      var message = StripAnsi(ex.Message);

      Xunit.Assert.Contains("order mismatch", message);
      Xunit.Assert.Contains("orderID = 10", message);
    }

    [Fact]
    public async Task Context_only_overload_is_reported()
    {
      var orderID = 10;

      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert(async () => await ValueAsync(1) == 2, () => orderID));

      Xunit.Assert.Contains("orderID = 10", StripAnsi(ex.Message));
    }

    [Fact]
    public async Task Throwing_awaited_call_reports_the_exception_with_source_text()
    {
      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert(async () => await ThrowingAsync() == 1));

      var message = StripAnsi(ex.Message);

      Xunit.Assert.Contains("await ThrowingAsync() == 1", message);
      Xunit.Assert.Contains("boom", message);
    }

    [Fact]
    public async Task Synchronous_throw_inside_async_body_gets_cause_attribution()
    {
      var values = new List<int>();

      // The throwing fragment (values.First()) contains no await, so its exception step
      // is recorded and the cause is attributed even inside an async body.
      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert(async () => await ValueAsync(values.First()) == 1));

      Xunit.Assert.Contains("values.First()", StripAnsi(ex.Message));
    }

    [Fact]
    public async Task Unnameable_awaitable_type_is_awaited_reflectively()
    {
      // Task of an anonymous type cannot be named in generated code, so re-evaluation
      // goes through GeneratedAssert.AwaitResult (await as object + reflective Result read).
      var task = Task.FromResult(new { Name = "actual" });

      await ShouldFailAsync(() => Assert(async () => (await task).Name == "expected"),
        "(await task).Name: \"expected\"", "(await task).Name: \"actual\"");
    }

    [Fact]
    public async Task Local_function_operand_degrades_to_source_text()
    {
      // Local functions are not members of their containing type, so generated code can
      // neither call nor reflectively resolve them: the assertion reports source text
      // only. (Regression: the generator used to emit Program.LocalValueAsync(...).)
      static Task<int> LocalValueAsync(int value) => Task.FromResult(value);

      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(
        () => Assert(async () => await LocalValueAsync(1) == 2));

      Xunit.Assert.Contains("await LocalValueAsync(1) == 2", StripAnsi(ex.Message));
    }

    [Fact]
    public async Task Stored_async_delegate_reports_the_unintercepted_marker()
    {
      Func<Task<bool>> stored = async () => await ValueAsync(1) == 2;

      var ex = await Xunit.Assert.ThrowsAnyAsync<Exception>(() => Assert(stored));

      Xunit.Assert.Contains("was not intercepted", StripAnsi(ex.Message));
    }

    [Fact]
    public async Task Stored_async_delegate_passes_silently()
    {
      Func<Task<bool>> stored = async () => await ValueAsync(1) == 1;

      await Assert(stored);
    }
  }
}
