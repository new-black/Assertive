using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Assertive.Config;
using Assertive.Runtime;
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

      ShouldEqual(() => list.Any(l => l == value));

      ShouldEqual(() => list.Any(l => l == "4"));

      var length = 2;
      var ending = "4";

      ShouldEqual(() => list.Count(l => l.Length == length && l.EndsWith(ending)) == 1);
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

      ShouldFail(() => customers.Count() == expectedCustomers);
    }

    [Fact]
    public void Locals_that_are_already_part_of_the_output_are_not_rendered_again()
    {
      var a = "abc";
      var b = "def";

      ShouldFail(() => a == b);
    }


    [Fact]
    public void Using_a_local_multiple_times_does_not_render_it_multiple_times()
    {
      var list = Enumerable.Range(0, 8).ToList();
      var expected = 25;

      ShouldFail(() => list[list.Count - 1] == expected * 2);
    }

    [Fact]
    public void Only_locals_that_have_not_already_been_outputted_are_rendered()
    {
      var list = Enumerable.Range(0, 8);
      var six = 6;

      ShouldFail(() => list.Count() == six);
    }

    [AssertionWrapper]
    internal void ShouldEqual(Func<bool> assertion,
      [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
      [CallerFilePath] string callerFilePath = "",
      [CallerLineNumber] int callerLineNumber = 0)
      => ShouldEqual(AssertionHandle.Degraded(assertion, assertionExpression), assertionExpression, callerFilePath, callerLineNumber);

    internal void ShouldEqual(AssertionHandle assertion, string assertionExpression = "", string callerFilePath = "", int callerLineNumber = 0)
    {
      try
      {
        assertion.Assert();
        Xunit.Assert.Fail("Expected assertion to fail.");
      }
      catch (Exception ex)
      {
        Assert.Snapshot(StripAnsi(ex.Message),
          options: $"L{callerLineNumber}",
          expression: assertionExpression,
          sourceFile: callerFilePath);
      }
    }
  }
}
