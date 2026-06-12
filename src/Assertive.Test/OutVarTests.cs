using System.Collections.Generic;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Assertions containing out-var declarations in a &&-only chain.
  /// The out variable declared in one conjunct (dict.TryGetValue(k, out var v)) is
  /// available for decomposition in subsequent conjuncts (v.Length > 3).
  /// </summary>
  public class OutVarTests : AssertionTestBase
  {
    // TryGetValue fails (key not present) — the leaf reports a plain failure (no expected/actual
    // from a bool-returning method call); verify the assertion throws.
    [Fact]
    public void Reports_failure_when_try_method_returns_false()
    {
      var dict = new Dictionary<string, string> { ["a"] = "hello" };
      ShouldFail(() => dict.TryGetValue("missing", out var val) && val.Length > 3);
    }

    // TryGetValue succeeds but subsequent check fails
    [Fact]
    public void Reports_subsequent_failure_when_try_method_succeeds()
    {
      var dict = new Dictionary<string, string> { ["key"] = "hi" };
      ShouldFail(() => dict.TryGetValue("key", out var val) && val.Length > 3);
    }

    // All passing
    [Fact]
    public void Does_not_throw_when_assertion_passes()
    {
      var dict = new Dictionary<string, string> { ["key"] = "hello" };
      Assert(() => dict.TryGetValue("key", out var val) && val.Length > 3);
    }

    // Int.TryParse fails — plain failure (no expected/actual from bool-returning call); verify throws.
    [Fact]
    public void Reports_parse_failure_when_int_try_parse_fails()
    {
      var input = "notanumber";
      ShouldFail(() => int.TryParse(input, out var n) && n > 0);
    }

    [Fact]
    public void Reports_subsequent_failure_when_parse_succeeds_but_check_fails()
    {
      var input = "-5";
      ShouldFail(() => int.TryParse(input, out var n) && n > 0);
    }

    [Fact]
    public void Passing_try_parse_chain_does_not_throw()
    {
      var input = "42";
      Assert(() => int.TryParse(input, out var n) && n > 0);
    }
  }
}
