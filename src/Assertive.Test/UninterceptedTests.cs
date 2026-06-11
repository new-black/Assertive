using System;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Call sites the generator cannot intercept (stored delegates, statement-body lambdas)
  /// fall back to CallerArgumentExpression source text with no decomposition or locals.
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

    [Fact]
    public void Statement_body_lambda_shows_block_source_only()
    {
      var x = 5;
      ShouldFail(() => { return x > 10; });
    }
  }
}
