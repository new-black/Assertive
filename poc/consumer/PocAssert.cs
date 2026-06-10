using System.Runtime.CompilerServices;

namespace Assertive.Poc;

public sealed class PocAssertionException(string message) : Exception(message);

public static class PocAssert
{
  /// <summary>
  /// The non-intercepted implementation: this is what runs when the source generator is
  /// not active (or a call site couldn't be intercepted). It can only report the source
  /// text via [CallerArgumentExpression] — no decomposition. The generator intercepts
  /// calls to this method and substitutes a per-call-site decomposed implementation.
  /// </summary>
  public static void That(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string expr = "")
  {
    if (!condition())
    {
      throw new PocAssertionException($"Assertion failed (runtime fallback, generator inactive): {expr}");
    }
  }
}
