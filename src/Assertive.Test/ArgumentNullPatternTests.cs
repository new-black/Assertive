using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class ArgumentNullPatternTests : AssertionTestBase
  {
    [Fact]
    public void Cause_of_ArgumentNull_is_found_single_level()
    {
      List<string>? items = null;
      
      ShouldFail(() => items.Where(i => i.StartsWith("abc")).Any(), "ArgumentNullException caused by calling Where on items which was null.");
    }
    
    [Fact]
    public void Cause_of_ArgumentNull_is_found_two_levels()
    {
      List<string>? items = null;

      ShouldFail(() => items.Where(i => i.StartsWith("abc")).Count(x => x.EndsWith("123")) == 0, "ArgumentNullException caused by calling Where on items which was null.");
    }
  }
}