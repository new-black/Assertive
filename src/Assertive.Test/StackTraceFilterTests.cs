using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Sdk;

namespace Assertive.Test
{
  public class StackTraceFilterTests : AssertionTestBase
  {
    private static readonly string[] _filteredPatterns =
    [
      "System.Linq.Expressions.Interpreter.",
      "System.Dynamic.Utils.",
      "Assertive.AssertImpl.",
      "Assertive.Assert."
    ];

    [Fact]
    public void Stack_trace_filters_out_internal_frames()
    {
      
      XunitException? caught = null;
      try
      {
        var list = new List<int>();

        Assert.That(() => list.Single() == 1);
      }
      catch (XunitException ex)
      {
        caught = ex;
      }

      var message = StripAnsi(caught!.Message);

      Assert.That(() => message.Contains("STACKTRACE")
                        && _filteredPatterns.All(p => !message.Contains(p))
                        && message.Contains("System.Linq.ThrowHelper"));
    }
  }
}
