using System;
using System.Text.RegularExpressions;
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
  public class VerbatimIdentifierTests
  {
    private static string StripAnsi(string input) => Regex.Replace(input, @"\u001b\[[0-9;]*[A-Za-z]", "");

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

      try
      {
        Assert(() => @lock.LockAcquired);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Xunit.Assert.Contains("@lock.LockAcquired", StripAnsi(ex.Message));
      }
    }

    [Fact]
    public void Keyword_named_local_in_equality_is_decomposed()
    {
      var @event = "raised";

      try
      {
        Assert(() => @event == "handled");
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        var expected = StripAnsi(string.Join("\n", (string[])ex.Data["Assertive.Expected"]!));
        Xunit.Assert.Equal("@event: \"handled\"", expected);
      }
    }

    [Fact]
    public void Keyword_named_local_as_whole_operand_is_not_repeated_in_locals()
    {
      var @lock = new Latch();
      Latch? other = null;

      try
      {
        Assert(() => @lock == other);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        // @lock is the whole left operand: its value is already displayed, so it must not
        // also be listed under LOCALS (name-vs-display comparison has to ignore the @).
        Xunit.Assert.DoesNotContain("lock: ", StripAnsi(ex.Message).Split("LOCALS").Length > 1
          ? StripAnsi(ex.Message).Split("LOCALS")[1]
          : "");
      }
    }
  }
}
