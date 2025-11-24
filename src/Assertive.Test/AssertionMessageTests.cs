using System;
using Assertive.Config;
using Xunit;

namespace Assertive.Test
{
  public class AssertionMessageTests : AssertionTestBase
  {
    [Fact]
    public void Message_is_part_of_output()
    {
      var order = new
      {
        Amount = 10
      };
      
      ShouldFailWithMessage(() => order.Amount > 20, "Expected orderID to be more than 20", """
                                                                                               MESSAGE                                                                      
                                                                                            Expected orderID to be more than 20
                                                                                             ℹ LOCALS                                                                       
                                                                                            order = { Amount = 10 }
                                                                                            """);
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
      
      ShouldFailWithMessage(() => order.Amount > 20, order, """
                                                               MESSAGE                                                                      
                                                            { ID = 999, Amount = 10 }
                                                             ℹ LOCALS                                                                       
                                                            order = { ID = 999, Amount = 10 }
                                                            """);
    }
    
    [Fact]
    public void Message_of_a_anonymous_type_is_part_of_output()
    {
      var order = new
      {
        ID = 999,
        Amount = 10
      };
      
      ShouldFailWithMessage(() => order.Amount > 20, order, """
                                                               MESSAGE                                                                      
                                                            { ID = 999, Amount = 10 }
                                                             ℹ LOCALS                                                                       
                                                            order = { ID = 999, Amount = 10 }
                                                            """);
    }
  }
}