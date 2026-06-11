using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  internal record NcUser(string Name, NcAddress? Address);
  internal record NcAddress(string City, string? Zip);

  /// <summary>
  /// Assertions using null-conditional access (?.): Assert(() => user?.Name == "Bob").
  /// When the receiver is null the whole chain short-circuits to null; the exception
  /// analysis should surface the null receiver as the cause.
  /// </summary>
  public class NullConditionalTests : AssertionTestBase
  {
    [Fact]
    public void Null_conditional_equality_fails_when_property_differs()
    {
      var user = new NcUser("Alice", null);
      ShouldFail(() => user?.Name == "Bob");
    }

    [Fact]
    public void Null_conditional_equality_fails_when_receiver_is_null()
    {
      NcUser? user = null;
      ShouldFail(() => user?.Name == "Bob");
    }

    [Fact]
    public void Null_conditional_chained_equality_fails_when_intermediate_is_null()
    {
      var user = new NcUser("Bob", null);
      ShouldFail(() => user?.Address?.City == "NYC");
    }

    [Fact]
    public void Null_conditional_chained_equality_fails_when_leaf_differs()
    {
      var user = new NcUser("Bob", new NcAddress("London", null));
      ShouldFail(() => user?.Address?.City == "NYC");
    }

    [Fact]
    public void Null_conditional_in_conjunction()
    {
      var user = new NcUser("Bob", null);
      var flag = true;
      ShouldFail(() => flag && user?.Address?.City == "NYC");
    }

    [Fact]
    public void Passing_null_conditional_does_not_throw()
    {
      var user = new NcUser("Bob", new NcAddress("NYC", "10001"));
      Assert(() => user?.Address?.City == "NYC");
    }
  }
}
