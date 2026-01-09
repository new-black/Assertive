using Xunit;

namespace Assertive.Test
{
  public class ReferenceEqualsPatternTests : AssertionTestBase
  {
    [Fact]
    public void ReferenceEquals_pattern()
    {
      var instance1 = new object();
      var instance2 = new object();
      
      ShouldFail(() => ReferenceEquals(instance1, instance2), "instance1 and instance2 should be the same instance.", @"instance1: System.Object
instance2: System.Object");
    }
    
    [Fact]
    public void ReferenceEquals_pattern_as_call_from_object()
    {
      var instance1 = new object();
      var instance2 = new object();
      
      ShouldFail(() => object.ReferenceEquals(instance1, instance2), "instance1 and instance2 should be the same instance.", @"instance1: System.Object
instance2: System.Object");
    }
    
    [Fact]
    public void Not_ReferenceEquals_pattern()
    {
      var instance1 = new object();
      var instance2 = instance1;
      
      ShouldFail(() => !ReferenceEquals(instance1, instance2), "instance1 and instance2 should be different instances.", "");
    }
  }
}