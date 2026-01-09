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

      ShouldFail(() => list[0].Contains("foo"), @"`list[0]` should contain the substring ""foo"".", @"`list[0]`: ""a""");
      ShouldFail(() => list[0].Contains(myValue), @"`list[0]` should contain the substring `myValue` (value: ""abc"").", @"`list[0]`: ""a""");
      ShouldFail(() => list.All(l => l.Length > 10), "All items of `list` should match the filter `l.Length > 10`", @"These 3 items did not:");
    }

    public void Dispose()
    {
      Configuration.ExpressionQuotationPattern = null;
    }
  }
}