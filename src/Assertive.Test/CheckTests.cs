using System.Threading.Tasks;
using Xunit;

namespace Assertive.Test;

public class CheckTests
{
  [Fact]
  public async Task Check_simple_object()
  {
    var object1 = new { Name = "John", Age = 30 };

    await Assert.Check(object1);
  }
}