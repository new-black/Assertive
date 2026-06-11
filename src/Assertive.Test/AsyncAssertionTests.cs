using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    // Mixed accessibilities on purpose — each pins a different re-evaluation strategy:
    // private goes through GeneratedAssert.InvokeStatic (reflective, accessibility
    // ignored), internal is emitted as a direct typed static call, and member access on
    // a captured local (Fetcher below) pastes typed as written.
    private static Task<int> ValueAsync(int value) => Task.FromResult(value);

    private static Task<List<int>> ListAsync() => Task.FromResult(new List<int> { 1, 2, 3 });

    internal static Task<string?> NullStringAsync() => Task.FromResult<string?>(null);

    public sealed class Fetcher
    {
      public Task<int> GetAsync(int value) => Task.FromResult(value);
    }

    private static async Task<int> ThrowingAsync()
    {
      await Task.Yield();
      throw new InvalidOperationException("boom");
    }

    private async Task ShouldFailAsync(Func<Task> assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
    {
      var ex = await CaptureFailureAsync(assertion);
      Assert.Snapshot(StripAnsi(ex.Message),
        options: $"L{callerLineNumber}",
        expression: assertionExpression,
        sourceFile: callerFilePath);
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

      await ShouldFailAsync(() => Assert(async () => await ValueAsync(10) == expected));
    }

    [Fact]
    public async Task Assert_That_overload_is_also_intercepted()
    {
      var expected = 5;

      await ShouldFailAsync(() => Assertive.Assert.That(async () => await ValueAsync(10) == expected));
    }

    [Fact]
    public async Task Member_access_awaited_operand_is_pasted_typed()
    {
      var fetcher = new Fetcher();

      await ShouldFailAsync(() => Assert(async () => await fetcher.GetAsync(3) == 5));
    }

    [Fact]
    public async Task Null_check_on_awaited_operand()
    {
      await ShouldFailAsync(() => Assert(async () => await NullStringAsync() != null));
    }

    [Fact]
    public async Task And_chain_reports_the_failing_conjunct()
    {
      var a = 1;

      await ShouldFailAsync(() => Assert(async () => a == 1 && await ValueAsync(7) == 2));
    }

    [Fact]
    public async Task Count_comparison_on_awaited_collection()
    {
      await ShouldFailAsync(() => Assert(async () => (await ListAsync()).Count == 2));
    }

    [Fact]
    public async Task Captured_locals_are_reported()
    {
      var threshold = 5;

      var ex = await CaptureFailureAsync(
        () => Assert(async () => await ValueAsync(1) > threshold + 2));

      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Message_and_context_are_reported()
    {
      var orderID = 10;

      var ex = await CaptureFailureAsync(
        () => Assert(async () => await ValueAsync(1) == 2, "order mismatch", () => orderID));

      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Context_only_overload_is_reported()
    {
      var orderID = 10;

      var ex = await CaptureFailureAsync(
        () => Assert(async () => await ValueAsync(1) == 2, () => orderID));

      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Throwing_awaited_call_reports_the_exception_with_source_text()
    {
      var ex = await CaptureFailureAsync(
        () => Assert(async () => await ThrowingAsync() == 1));

      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Synchronous_throw_inside_async_body_gets_cause_attribution()
    {
      var values = new List<int>();

      var ex = await CaptureFailureAsync(
        () => Assert(async () => await ValueAsync(values.First()) == 1));

      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Unnameable_awaitable_type_is_awaited_reflectively()
    {
      var task = Task.FromResult(new { Name = "actual" });

      await ShouldFailAsync(() => Assert(async () => (await task).Name == "expected"));
    }

    [Fact]
    public async Task Local_function_operands_are_lifted_into_the_generated_code()
    {
      static Task<int> LocalValueAsync(int value) => Task.FromResult(value);

      await ShouldFailAsync(() => Assert(async () => await LocalValueAsync(1) == 2));
    }

    [Fact]
    public async Task Capturing_local_function_binds_to_the_scope_captures()
    {
      var factor = 10;
      Task<int> ScaledAsync(int value) => Task.FromResult(value * factor);

      await ShouldFailAsync(() => Assert(async () => await ScaledAsync(2) == 5));
    }

    [Fact]
    public async Task Local_functions_lift_transitively()
    {
      static int Twice(int value) => value * 2;
      static Task<int> TwiceAsync(int value) => Task.FromResult(Twice(value));

      await ShouldFailAsync(() => Assert(async () => await TwiceAsync(3) == 5));
    }

    [Fact]
    public async Task Tuple_literal_with_awaited_element_is_decomposed()
    {
      await ShouldFailAsync(() => Assert(async () => (await ValueAsync(1), 2) == (9, 2)));
    }

    [Fact]
    public async Task Stored_async_delegate_reports_the_unintercepted_marker()
    {
      Func<Task<bool>> stored = async () => await ValueAsync(1) == 2;

      var ex = await CaptureFailureAsync(() => Assert(stored));

      SnapshotMessage(ex);
    }

    [Fact]
    public async Task Stored_async_delegate_passes_silently()
    {
      Func<Task<bool>> stored = async () => await ValueAsync(1) == 1;

      await Assert(stored);
    }
  }
}
