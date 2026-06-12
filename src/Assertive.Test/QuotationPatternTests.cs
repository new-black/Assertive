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

      ShouldFail(() => list[0].Contains("foo"));
      ShouldFail(() => list[0].Contains(myValue));
      ShouldFail(() => list.All(l => l.Length > 10));
    }

    public void Dispose()
    {
      Configuration.ExpressionQuotationPattern = null;
    }
  }
}