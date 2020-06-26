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
      
      ShouldFail(() => ReferenceEquals(instance1, instance2), "Expected instance1 and instance2 to be the same instance.");
    }
    
    [Fact]
    public void ReferenceEquals_pattern_as_call_from_object()
    {
      var instance1 = new object();
      var instance2 = new object();
      
      ShouldFail(() => object.ReferenceEquals(instance1, instance2), "Expected instance1 and instance2 to be the same instance.");
    }
    
    [Fact]
    public void Not_ReferenceEquals_pattern()
    {
      var instance1 = new object();
      var instance2 = instance1;
      
      ShouldFail(() => !ReferenceEquals(instance1, instance2), "Expected instance1 and instance2 to be a different instances.");
    }
  }
}