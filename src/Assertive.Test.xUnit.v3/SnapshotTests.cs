namespace Assertive.Test.xUnit.v3;
using static DSL;

public class SnapshotTests
{
  [Fact]
  public void Can_use_snapshot_testing_in_xUnit_v3()
  {
    var obj = new MyObject
    {
      Foo = "foo",
      Bar = "bar"
    };

    Assert(obj);
  }

  public class MyObject
  {
    public string Foo { get; set; } = "";
    public string Bar { get; set; } = "";
  }
}
