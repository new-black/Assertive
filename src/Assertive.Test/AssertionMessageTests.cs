using Xunit;

namespace Assertive.Test
{
  public class AssertionMessageTests : AssertionTestBase
  {
    [Fact]
    public void Message_is_part_of_output()
    {
      int orderID = 10;
      
      var order = new
      {
        Amount = 10
      };
      
      ShouldFail(() => order.Amount > 20, "Expected orderID to be more than 20", "Message: Expected orderID to be more than 20");
    }
  }
}