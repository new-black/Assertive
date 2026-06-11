using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Closure capture under JIT optimization. Reflective capture reads
  /// (GeneratedAssert.GetCapturedValue walking delegate.Target) assume the assertion
  /// lambda's display class is a real object reachable from the delegate. Newer JITs
  /// (escape analysis, .NET 10/11) can stack-allocate delegates and closures that
  /// provably do not escape — these tests heat the asserting method past tier-1
  /// promotion so that, if the JIT were ever able to apply that to a lambda passed to
  /// Assert, decomposition would catch it here. The suite also runs under
  /// DOTNET_TieredCompilation=0 in CI-style verification, which compiles everything
  /// with full optimizations immediately.
  /// </summary>
  public class ClosureCaptureJitTests : AssertionTestBase
  {
    [Fact]
    public void Captures_survive_tier1_promotion_of_the_asserting_method()
    {
      // Promote AssertOnce past the tiering threshold (default 30 calls), give the
      // background tier-1 compile time to land, then keep calling so the optimized
      // code is actually what runs when the failure finally happens.
      for (var i = 0; i < 200; i++)
      {
        AssertOnce(10, 10);
      }

      Thread.Sleep(200);

      for (var i = 0; i < 200; i++)
      {
        AssertOnce(10, 10);
      }

      var ex = CaptureFailure(() => AssertOnce(3, 7));

      SnapshotMessage(ex);
    }

    [Fact]
    public void Captures_survive_a_hot_loop_in_a_single_method_body()
    {
      // OSR path: the loop body containing the assertion lambda gets recompiled with
      // full optimizations mid-loop; closure allocations in later iterations come from
      // the optimized code.
      var failures = 0;

      for (var i = 0; i < 100_000; i++)
      {
        var value = i;
        var limit = 100_000;

        try
        {
          Assert(() => value < limit && value == i);
        }
        catch
        {
          failures++;
        }
      }

      Xunit.Assert.Equal(0, failures);

      var final = 5;
      var expected = 6;

      ShouldFail(() => final == expected);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertOnce(int value, int expected)
    {
      Assert(() => value == expected);
    }
  }
}
