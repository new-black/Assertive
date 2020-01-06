using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using FluentAssertions;
using FluentAssertions.Common;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class AssertThatTests : AssertionTestBase
  {
    [Fact]
    public void Lambda_expression_in_assert_works()
    {
      var list = new List<int>();

      ShouldFail(() => list.Any(x => x > 1), "Assertion failed: list.Any(x => x > 1)");
      ShouldFail(() => list.Count == 0 && list.Any(x => x > 1), "Assertion failed: list.Any(x => x > 1)");
    }

    [Fact]
    public void Assertion_that_throw_tests()
    {
      StringBuilder sb = null;
      var array = new int[0];

      ShouldFail(() => sb.Append("a") != null,
        "NullReferenceException caused by calling Append on sb which was null.");
      ShouldFail(() => array[1] == 1, "Assertion threw System.IndexOutOfRangeException:");
    }

    [Fact]
    public void Throws_tests()
    {
      StringBuilder sb = null;
      var array = new int[0];

      Assert.Throws(() => sb.Append("A"));
      Assert.Throws(() => array[1]);
      Assert.Throws(() => int.Parse("abc"));

      Assert.Throws<NullReferenceException>(() => sb.Append("A"));
      Assert.Throws<IndexOutOfRangeException>(() => array[1]);
      Assert.Throws<FormatException>(() => int.Parse("abc"));
    }

    [Fact]
    public void Failing_Throws_tests()
    {
      StringBuilder sb = new StringBuilder();
      var array = new int[10];

      ShouldThrow(() => sb.Append("A"), @"Expected sb.Append(""A"") to throw an exception, but it did not.");
      ShouldThrow(() => array[1], @"Expected array[1] to throw an exception, but it did not.");
      ShouldThrow(() => int.Parse("123"), @"Expected int.Parse(""123"") to throw an exception, but it did not.");

      ShouldThrow<NullReferenceException>(() => sb.Append("A"), @"Expected sb.Append(""A"") to throw an exception, but it did not.");
      ShouldThrow<IndexOutOfRangeException>(() => array[1], @"Expected array[1] to throw an exception, but it did not.");
      ShouldThrow<FormatException>(() => int.Parse("123"), @"Expected int.Parse(""123"") to throw an exception, but it did not.");
    }
    
    [Fact]
    public void Failing_Throws_when_type_mismatch_tests()
    {
      StringBuilder sb = null;
      var array = new int[0];

      ShouldThrow<InvalidOperationException>(() => sb.Append("A"), @"Expected sb.Append(""A"") to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.NullReferenceException instead.");
      ShouldThrow<InvalidOperationException>(() => array[1], @"Expected array[1] to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.IndexOutOfRangeException instead.");
      ShouldThrow<InvalidOperationException>(() => int.Parse("abc"), @"Expected int.Parse(""abc"") to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.FormatException instead.");
    }

    [Fact]
    public void LessThanOrGreaterThanPattern_tests()
    {
      var one = 1;
      var two = 2;
      
      Assert.That(() => one < two);
      Assert.That(() => one <= two);
      Assert.That(() => two > one);
      Assert.That(() => two >= one);
     
      ShouldFail(() => two < one, "Expected two to be less than one, but two was 2 while one was 1.");
      ShouldFail(() => two <= one, "Expected two to be less than or equal to one, but two was 2 while one was 1.");
      ShouldFail(() => one > two, "Expected one to be greater than two, but one was 1 while two was 2.");
      ShouldFail(() => one >= two, "Expected one to be greater than or equal to two, but one was 1 while two was 2.");
    }

    [Fact]
    public void EqualsPattern_tests()
    {
      var a = "A";
      var b = "B";

      var x = 1;
      var y = 2;

      var foo = "foo";
      var bar = "bar";

      Assert.That(() => a != b);
      Assert.That(() => a == "A");
      Assert.That(() => x != y);
      Assert.That(() => x == 1);
      Assert.That(() => foo + bar == "foobar");

      ShouldFail(() => a == b, @"Expected a to equal b but a was ""A"" while b was ""B"".");
      ShouldFail(() => x == y, "Expected x to equal y but x was 1 while y was 2.");
      ShouldFail(() => foo + bar == "barfoo",
        @"Expected foo + bar to equal ""barfoo"" but foo + bar was ""foobar"".");
      ShouldFail(() => a == "B", @"Expected a to equal ""B"" but a was ""A"".");
    }

    [Fact]
    public void NullPattern_tests()
    {
      string nullString = null;

      string notNullString = "a string";
      
      ShouldFail(() => nullString != null, "Expected nullString to not be null but it was null.");
      ShouldFail(() => notNullString == null, @"Expected notNullString to be null but it was ""a string"" instead.");
    }
    
    [Fact]
    public void ContainsPattern_tests()
    {
      var list = new List<string>
      {
        "a", "b", "c"
      };

      var myValue = "abc";
      
      ShouldFail(() => list.Contains("d"), @"Expected list to contain ""d"" but it did not.");
      ShouldFail(() => list.Contains(myValue), @"Expected list to contain myValue but it did not.");
    }

    [Fact]
    public void LengthPattern_tests()
    {
      var list = new List<string>
      {
        "a", "b"
      };

      var array = new int[2];

      ShouldFail(() => list.Count == 3, "Expected list to have a count equal to 3 but the actual count was 2.");
      ShouldFail(() => array.Length > 3, "Expected array to have a length greater than 3 but the actual length was 2.");
      ShouldFail(() => list.Count() <= 1, "Expected list to have a count less than or equal to 1 but the actual count was 2.");
      ShouldFail(() => list.Count() > array.Length, "Expected list to have a count greater than array.Length (2) but the actual count was 2.");
    }
  }
}