using System;
using System.Threading;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Locals named with verbatim identifiers (@lock, @event) have a symbol name without the
  /// @ ("lock"), while pasted operand source keeps the @ as written. The generated capture
  /// declarations must escape the name, or the generated file fails to compile.
  /// </summary>
  public class VerbatimIdentifierTests : AssertionTestBase
  {
    private class Latch
    {
      public bool LockAcquired => false;
    }

    [Fact]
    public void Keyword_named_locals_compile_and_pass()
    {
      Exception? exception = new InvalidOperationException();
      var callsToSet = 5;
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      var @lock = new Latch();

      Assert(() => exception != null && callsToSet == 5 && cts.IsCancellationRequested && !@lock.LockAcquired);
    }

    [Fact]
    public void Keyword_named_local_is_decomposed_on_failure()
    {
      var @lock = new Latch();

      ShouldFail(() => @lock.LockAcquired);
    }

    [Fact]
    public void Keyword_named_local_in_equality_is_decomposed()
    {
      var @event = "raised";

      ShouldFail(() => @event == "handled");
    }

    [Fact]
    public void Keyword_named_local_as_whole_operand_is_not_repeated_in_locals()
    {
      var @lock = new Latch();
      Latch? other = null;

      ShouldFail(() => @lock == other);
    }
  }
}
