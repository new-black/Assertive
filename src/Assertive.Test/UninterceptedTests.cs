using System;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Call sites the generator cannot intercept (stored delegates, named arguments) have no
  /// source text: the public API no longer captures it via CallerArgumentExpression — the
  /// interceptor embeds it instead. The failure says so explicitly rather than silently
  /// looking like a healthy report.
  /// </summary>
  public class UninterceptedTests : AssertionTestBase
  {
    [Fact]
    public void Stored_delegate_reports_the_unintercepted_marker()
    {
      var x = 1;
      Func<bool> stored = () => x == 2;

      ShouldFail(stored);
    }

    [Fact]
    public void Stored_delegate_passes_silently()
    {
      var x = 1;
      Func<bool> stored = () => x == 1;

      Assert(stored);
    }
  }
}
