namespace Assertive.Runtime
{
  /// <summary>
  /// Entry point for assertion wrapper implementations
  /// (see <see cref="AssertionWrapperAttribute"/>).
  /// </summary>
  [System.Diagnostics.StackTraceHidden]
  public static class AssertionHandleExtensions
  {
    /// <summary>
    /// Asserts an assertion received through an assertion wrapper
    /// (see <see cref="AssertionWrapperAttribute"/>). Uses the generated assertion when the
    /// wrapper call site was intercepted; otherwise evaluates the plain delegate.
    /// </summary>
    /// <param name="assertion">The assertion handle to evaluate.</param>
    public static void Assert(this AssertionHandle assertion)
    {
      if (assertion.GeneratedAssertion is { } generated)
      {
        generated();
        return;
      }

      Assertive.Assert.ThatCore(assertion.Condition!, null, null,
        string.IsNullOrEmpty(assertion.SourceText) ? Assertive.Assert.UninterceptedSource : assertion.SourceText!, null);
    }
  }
}
