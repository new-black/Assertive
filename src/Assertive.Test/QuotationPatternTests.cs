using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
using Xunit;

namespace Assertive.Test
{
  public class QuotationPatternTests : AssertionTestBase, IDisposable
  {
    [Fact]
    public void QuotationPattern_works()
    {
      var list = new List<string>
      {
        "a", "b", "c"
      };

      var myValue = "abc";

      Configuration.ExpressionQuotationPattern = ExpressionQuotationPatterns.Backticks;

      ShouldFail(() => list[0].Contains("foo"), @"Expected `list[0]` (value: ""a"") to contain ""foo"".");
      ShouldFail(() => list[0].Contains(myValue), @"Expected `list[0]` (value: ""a"") to contain `myValue`");
      ShouldFail(() => list.All(l => l.Length > 10), "Expected all items of `list` to match the filter `l.Length > 10`, but these 3 items did not:");
    }

    public void Dispose()
    {
      Configuration.ExpressionQuotationPattern = null;
    }
  }
}