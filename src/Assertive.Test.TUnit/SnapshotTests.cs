namespace Assertive.Test.TUnit;
using static DSL;

public class SnapshotTests
{
  [Test]
  public void Can_use_snapshot_testing_in_TUnit()
  {
    var obj = new MyObject()
    {
      Foo = "foo",
      Bar = "bar"
    };

    Assert(obj);
  }

  public class MyObject
  {
    public string Foo { get; set; }
    public string Bar { get; set; }
  }
}