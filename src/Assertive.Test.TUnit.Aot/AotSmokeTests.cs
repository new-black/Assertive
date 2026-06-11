using System;
using Assertive.TestFrameworks;
using TUnit.Core;
using static Assertive.DSL;

namespace Assertive.Test.TUnit.Aot;

/// <summary>
/// Smoke tests that run inside a Native-AOT-published TUnit host. They confirm that, under
/// AOT, (1) the source-generated interceptors run, (2) a passing assertion is a no-op,
/// (3) a failing assertion still decomposes into a contextual message (the app assembly is
/// rooted, so the reflective reads of locals/closures succeed), and (4) Assertive's TUnit
/// detection works without the DLR (it uses typed reflection, not `dynamic`).
/// </summary>
public class AotSmokeTests
{
  [Test]
  public void Passing_assertion_is_a_no_op()
  {
    var amount = 50;

    Assert(() => amount == 50);
  }

  [Test]
  public void Failing_equality_is_decomposed()
  {
    var expected = 5;
    var actual = 10;

    var exception = Capture(() => Assert(() => actual == expected));

    Assert(() => exception != null);
    Assert(() => exception!.Data.Contains("Assertive.Expected"));
  }

  [Test]
  public void Failing_string_pattern_reports_the_contextual_message()
  {
    var value = "ab";

    var exception = Capture(() => Assert(() => value.Contains("z")));

    Assert(() => exception != null);

    var expected = ((string[])exception!.Data["Assertive.Expected"]!)[0];

    Assert(() => expected.Contains("should contain the substring"));
  }

  [Test]
  public void Captured_locals_are_reported()
  {
    var threshold = 5;
    var actual = 3;

    var exception = Capture(() => Assert(() => actual > threshold));

    Assert(() => exception != null);
    Assert(() => exception!.Message.Contains("threshold"));
  }

  [Test]
  public void TUnit_detection_degrades_gracefully_under_aot()
  {
    // TUnitFramework.GetCurrentTestInfo resolves the running test via typed reflection
    // (no `dynamic`, which would crash under Native AOT because the DLR is unavailable).
    // When TUnit's metadata types are trimmed away under AOT it simply returns null instead
    // of throwing — the graceful fallback snapshot naming relies on. The guarantee this pins
    // is that detection never throws, regardless of how aggressively the host is trimmed.
    var exception = Capture(() => { _ = new TUnitFramework().GetCurrentTestInfo(); });

    Assert(() => exception == null);
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
}
