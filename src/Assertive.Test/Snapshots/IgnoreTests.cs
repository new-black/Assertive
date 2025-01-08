using System;
using System.Diagnostics;
using Assertive.Config;
using Xunit;
using Xunit.Abstractions;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class IgnoreTests
{
  private readonly ITestOutputHelper _testOutputHelper;
  public IgnoreTests(ITestOutputHelper testOutputHelper)
  {
    _testOutputHelper = testOutputHelper;
  }

  [Fact]
  public void Can_ignore_properties()
  {
    var obj = new
    {
      ProductID1 = Random.Shared.NextInt64(),
      ProductID2 = Random.Shared.NextInt64(),
      ProductID3 = Random.Shared.NextInt64(),
      GuidValue = Guid.NewGuid(),
      Name = "foo"
    };
    
    Assert(obj, Configuration.Snapshots with { Normalization = Configuration.Snapshots.Normalization with { NormalizeGuid = false }, ShouldIgnore = ((property, _, value) =>
    {
      _testOutputHelper.WriteLine(property.Name);
      
      if (property.Name.StartsWith("ProductID")) return true;

      if (value is Guid) return true;

      return false;
    })});
  }
}