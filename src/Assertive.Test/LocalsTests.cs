using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class LocalsTests : AssertionTestBase
  {
    [Fact]
    public void Locals_are_rendered_correctly()
    {
      var list = new[]
      {
        "1", "2", "3"
      };

      var value = "4";
      
      ShouldEqual(() => list.Any(l => l == value), @"- list = [ ""1"", ""2"", ""3"" ]
- value = ""4""");

      ShouldEqual(() => list.Any(l => l == "4"), @"- list = [ ""1"", ""2"", ""3"" ]");

      var length = 2;
      var ending = "4";
      
      ShouldEqual(() => list.Count(l => l.Length == length && l.EndsWith(ending)) == 1, @"- list = [ ""1"", ""2"", ""3"" ]
- length = 2
- ending = ""4""");
    }

    [Fact]
    public void Locals_that_are_already_part_of_the_output_are_not_rendered_again()
    {
      var a = "abc";
      var b = "def";
      
      ShouldFail(() => a == b, @"Expected a to equal b but a was ""abc"" while b was ""def"".

Assertion: a == b
", true);
    }
    
    [Fact]
    public void Only_locals_that_have_not_already_been_outputted_are_rendered()
    {
      var list = Enumerable.Range(0, 8);
      
      ShouldFail(() => list.Count() == 6, @"Expected list to have a count equal to 6 but the actual count was 8.

Assertion: list.Count() == 6

Locals:

- list = [ 0, 1, 2, 3, 4, 5, 6, 7 ]

", true);
    }

    private void ShouldEqual(Expression<Func<bool>> assertion, string expected)
    {
      var result = LocalsProvider.LocalsToString(assertion, new HashSet<Expression>());
      
      Assert.That(() => result == expected);
    }
  }
}