using System;
using System.Text;
using Xunit;

namespace Assertive.Test
{
  public class ThrowsTests : AssertionTestBase
  {
    
    [Fact]
    public void Assertion_that_throw_tests()
    {
      StringBuilder? sb = null;
      var array = new int[0];

      ShouldFail(() => sb!.Append("a") != null,
        "NullReferenceException caused by calling Append on sb which was null.");
      ShouldFail(() => array[1] == 1, "IndexOutOfRangeException caused by accessing index 1 on array, actual length was 0.");
    }

    [Fact]
    public void Throws_tests()
    {
      StringBuilder? sb = null;
      var array = new int[0];

      Assert.Throws(() => sb!.Append("A"));
      Assert.Throws(() => array[1]);
      Assert.Throws(() => int.Parse("abc"));

      Assert.Throws<NullReferenceException>(() => sb!.Append("A"));
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
      StringBuilder? sb = null;
      var array = new int[0];

      ShouldThrow<InvalidOperationException>(() => sb!.Append("A"), @"Expected sb.Append(""A"") to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.NullReferenceException instead.");
      ShouldThrow<InvalidOperationException>(() => array[1], @"Expected array[1] to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.IndexOutOfRangeException instead.");
      ShouldThrow<InvalidOperationException>(() => int.Parse("abc"), @"Expected int.Parse(""abc"") to throw an exception of type System.InvalidOperationException, but it threw an exception of type System.FormatException instead.");
    }

  }
}