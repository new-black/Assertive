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
    
    private class Order
    {
      public int ID { get; set; }
      public decimal Amount { get; set; }
    }
    
    [Fact]
    public void Message_of_a_custom_object_is_part_of_output()
    {
      var order = new Order
      {
        ID = 999,
        Amount = 10
      };
      
      ShouldFail(() => order.Amount > 20, order, "Message: { ID = 999, Amount = 10 }");
    }
    
    [Fact]
    public void Message_of_a_anonymous_type_is_part_of_output()
    {
      var order = new
      {
        ID = 999,
        Amount = 10
      };
      
      ShouldFail(() => order.Amount > 20, order, "Message: { ID = 999, Amount = 10 }");
    }
  }
}