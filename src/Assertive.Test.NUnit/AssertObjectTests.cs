using DiffEngine;
using Assertive.Config;
using NUnit.Framework.Internal;
using static Assertive.DSL;

namespace Assertive.Test.NUnit;

public class AssertObjectTests 
{
  public class Customer
  {
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
    public Address Address { get; set; }
  }

  public class Address
  {
    public string Street;
    public string HouseNumber;
  }

  [Test]
  public void MyTest()
  {
    Configuration.Snapshots.LaunchDiffTool = (temp, target) =>
    {
      DiffRunner.Launch(temp, target);
    };

    Configuration.Snapshots.ExtraneousPropertiesOption = (_, _) => Configuration.ExtraneousPropertiesOptions.AutomaticUpdate;
    Configuration.Snapshots.ExcludeNullValues = true;

    Configuration.Snapshots.ValueRenderer = (prop, _) =>
    {
      return null;
    };
    
    Configuration.Snapshots.RegisterPlaceholderValidator("age", value => int.TryParse(value, out var val) && val > 0, "Age must be a positive number.");
    
    var obj = new Customer { FirstName = "Johan", LastName = "Doe", Age = 34};

    Assert(obj);

    obj.FirstName = "Bob";
    
    Assert(obj);
    
    Assert(new Customer { FirstName = "Johan", LastName = "Doe", Age = 28});
  }
  
  [Test]
  public void Bla()
  {
    Configuration.Snapshots.LaunchDiffTool = (temp, target) =>
    {
      DiffRunner.Launch(temp, target);
    };

    Configuration.Snapshots.ExtraneousPropertiesOption = (_, _) => Configuration.ExtraneousPropertiesOptions.AutomaticUpdate;
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
  
  //
  // [Test]
  // [TestCase("Johan", "Doe")]
  // [TestCase("Bob", "Bobby")]
  // public void Test1(string firstName, string lastName)
  // {
  //   Configuration.Snapshots.LaunchDiffTool = (temp, target) =>
  //   {
  //     DiffRunner.Launch(temp, target);
  //   };
  //
  //   Configuration.Snapshots.ExtraneousPropertiesOption = (_, _) => Configuration.ExtraneousPropertiesOptions.AutomaticUpdate;
  //   
  //   var obj = new Customer { FirstName = firstName, LastName = lastName };
  //
  //   Assert(obj);
  //
  //   obj.FirstName = obj.LastName;
  //   
  //   Assert(obj);
  //   
  //   Assert(new Customer { FirstName = firstName, LastName = lastName });
  // }
}