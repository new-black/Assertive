using Xunit;

namespace Assertive.Test
{
  public class AssertionContextTests : AssertionTestBase
  {
    [Fact]
    public void Context_is_part_of_output()
    {
      int orderID = 10;

      var order = new
      {
        Amount = 10
      };
      
      ShouldFail(() => order.Amount > 20, () => orderID, "Context: orderID = 10");
    }
    
    [Fact]
    public void Complex_context_object_is_serialized()
    {
      var order = new Order
      {
        ID = 99,
        Amount = 10
      };
      
      ShouldFail(() => order.Amount > 20, () => order, "Context: order = { ID = 99, Amount = 10 }");
    }

    [Fact]
    public void Anonymous_type_context_object_is_serialized()
    {
      var order = new
      {
        ID = 99,
        Amount = 10
      };
      
      ShouldFail(() => order.Amount > 20, () => order, "Context: order = { ID = 99, Amount = 10 }");
    }
    
    private class Order
    {
      public int ID { get; set; }
      public decimal Amount { get; set; }
    }
  }
}