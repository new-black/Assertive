using Xunit;

namespace Assertive.Test
{
  public class BoolPatternTests : AssertionTestBase
  {
    [Fact]
    public void Test_true()
    {
      var success = false;

      ShouldFail(() => success, "Expected success to be true.");
    }

    [Fact]
    public void Test_false()
    {
      var success = true;
      ShouldFail(() => !success, "Expected success to be false.");
    }

    [Fact]
    public void Test_member_access_true()
    {
      var foo = new Foo();

      ShouldFail(() => foo.Success, "Expected foo.Success to be true.");
    }

    [Fact]
    public void Test_member_access_false()
    {
      var foo = new Foo()
      {
        Success = true
      };

      ShouldFail(() => !foo.Success, "Expected foo.Success to be false.");
    }

    private class Foo
    {
      public bool Success { get; set; } = false;
    }
  }
}