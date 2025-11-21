using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Config;
using Assertive.Expressions;
using Assertive.Helpers;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class LocalsTests : AssertionTestBase, IDisposable
  {
    public LocalsTests()
    {
      Configuration.Colors.Enabled = false;
    }

    public void Dispose()
    {
      Configuration.Colors.Enabled = true;
    }

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

      ShouldEqual(() => list.Count(l => l.Length == length && l.EndsWith(ending)) == 1,
        @"- list = [ ""1"", ""2"", ""3"" ]
- length = 2
- ending = ""4""");
    }

    private class Customer
    {
      public int ID { get; set; }
      public string FirstName { get; set; }
    }

    [Fact]
    public void Locals_with_complex_objects()
    {
      var customers = new List<Customer>()
      {
        new Customer { ID = 1, FirstName = "John" },
        new Customer() { ID = 2, FirstName = "Bob" },
        new Customer() { ID = 3, FirstName = "Alice " }
      };

      var expectedCustomers = 2;

      try
      {
        Assert(() => customers.Count() == expectedCustomers);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Assert(() => ex.Message.Contains("""customers = [ { ID = 1, FirstName = "John" }, { ID = 2, FirstName = "Bob" }, { ID = 3, FirstName = "Alice " } ]"""));
      }
    }

    [Fact]
    public void Locals_that_are_already_part_of_the_output_are_not_rendered_again()
    {
      var a = "abc";
      var b = "def";

      try
      {
        Assert(() => a == b);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch(Exception ex)
      {
        Assert(() => !ex.Message.Contains("[LOCALS]"));
      }
    }
    
    
    [Fact]
    public void Using_a_local_multiple_times_does_not_render_it_multiple_times()
    {
      var list = Enumerable.Range(0, 8).ToList();
      var expected = 25;

      try
      {
        Assert(() => list[list.Count - 1] == expected * 2);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Assert(() => ex.Message.Contains("list = [ 0, 1, 2, 3, 4, 5, 6, 7 ]"));
        Assert(() => ex.Message.EndsWith("""
                                         list = [ 0, 1, 2, 3, 4, 5, 6, 7 ]
                                         expected = 25
                                         ················································································
                                         
                                         """));
      }
    }

    [Fact]
    public void Only_locals_that_have_not_already_been_outputted_are_rendered()
    {
      var list = Enumerable.Range(0, 8);
      var six = 6;

      try
      {
        Assert(() => list.Count() == six);
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Assert(() => ex.Message.Contains("list = [ 0, 1, 2, 3, 4, 5, 6, 7 ]"));
        Assert(() => !ex.Message.Contains("six = 6"));
      }
    }

    private void ShouldEqual(Expression<Func<bool>> assertion, string expected)
    {
      var result = LocalsProvider.LocalsToString(assertion, new HashSet<Expression>());

      Assert.That(() => result == expected);
    }
  }
}