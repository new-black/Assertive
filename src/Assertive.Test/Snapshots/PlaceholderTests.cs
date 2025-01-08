using System;
using Assertive.Config;
using DiffEngine;
using Xunit;
using Xunit.Sdk;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class PlaceholderTests
{
  [Fact]
  public void Can_use_unnumbered_placeholders_in_expected_files()
  {
    var obj = new
    {
      ProductID1 = Random.Shared.NextInt64(),
      ProductID2 = Random.Shared.NextInt64(),
      ProductID3 = Random.Shared.NextInt64(),
    };

    Assert(obj);
  }

  
  [Fact]
  public void Can_use_numbered_placeholders_in_expected_files()
  {
    var val1 = Random.Shared.NextInt64();
    var val2 = Random.Shared.NextInt64();

    var obj = new
    {
      ProductID1 = val1,
      ProductID2 = val2,
      ProductID3 = val1,
    };

    Assert(obj);
  }

  [Fact]
  public void Non_matching_placeholders_throw()
  {
    var val1 = Random.Shared.NextInt64();
    var val2 = Random.Shared.NextInt64();

    var obj = new
    {
      ProductID1 = val1,
      ProductID2 = val2,
      ProductID3 = val2,
    };

    bool throws = false;
    try
    {
      Assert(obj, Configuration.Snapshots with { LaunchDiffTool = null });
    }
    catch (XunitException ex) when (ex.Message.Contains("Expected '@@productid#1'"))
    {
      throws = true;
    }
    
    Assert(() => throws);
  }
  
  [Fact]
  public void Can_use_placeholder_validators()
  {
    var obj = new
    {
      ProductID = Random.Shared.NextInt64(),
      Price = (decimal)Random.Shared.Next(10, 100)
    };

    var config = Configuration.Snapshots with { };
    config.RegisterPlaceholderValidator("price", value => decimal.TryParse(value, out var price) && price > 0, "Price must be positive");
    
    Assert(obj, config);
    
    bool throws = false;
    try
    {
      obj = new
      {
        ProductID = Random.Shared.NextInt64(),
        Price = 0m
      };
      
      Assert(obj, config with { LaunchDiffTool = null });
    }
    catch (XunitException ex) when (ex.Message.Contains("Price must be positive"))
    {
      throws = true;
    }
    
    Assert(() => throws);
  }
}