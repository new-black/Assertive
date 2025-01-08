using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class ValueRendererTests
{
  [Fact]
  public void Can_use_ValueRenderer_to_customize_output()
  {
    var obj = new
    {
      ProductID = 1,
      Name = "Product Name",
    };

    var options = Configuration.Snapshots with
    {
      ValueRenderer = (property, _, value) =>
      {
        if (property.Name == "ProductID")
        {
          return "<ProductID>";
        }

        return value;
      }
    };

    Assert(obj, options);
  }
}