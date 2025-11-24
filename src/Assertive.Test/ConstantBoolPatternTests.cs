using Xunit;

namespace Assertive.Test
{
  public class BoolPatternTests : AssertionTestBase
  {
    [Fact]
    public void Test_true()
    {
      var success = false;

      ShouldFail(() => success, "success: true", "false");
    }

    [Fact]
    public void Test_false()
    {
      var success = true;
      ShouldFail(() => !success, "success: false", "true");
    }

    [Fact]
    public void Test_member_access_true()
    {
      var foo = new Foo();

      ShouldFail(() => foo.Success, "foo.Success: true", "false");
    }

    [Fact]
    public void Test_member_access_false()
    {
      var foo = new Foo()
      {
        Success = true
      };

      ShouldFail(() => !foo.Success, "foo.Success: false", "true");
    }

    private class Foo
    {
      public bool Success { get; set; } = false;
    }
  }
}