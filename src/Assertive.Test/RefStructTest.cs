using System;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test;

public class RefStructTest
{
  [Fact]
  public void RefStructs_method_overloads_can_be_used_in_assertions()
  {
    int[] ids = [1, 2, 3];
    
    Assert(() => Foo.Bar(ids));
  }
  
  [Fact]
  public void Can_use_Contains_overload_that_accepts_SpanT()
  {
    int[] ids = [1, 2, 3];
    
    Assert(() => MemoryExtensions.Contains(ids, 2));
  }

  public class Foo
  {
    public static bool Bar(Span<int> span)
    {
      return true;
    }
  }
}