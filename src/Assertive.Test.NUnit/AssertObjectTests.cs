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
  }

  [Test]
  public void Test()
  {
    Configuration.CheckSettings.LaunchDiffTool = (temp, target) =>
    {
      DiffRunner.Launch(temp, target);
    };

    Configuration.CheckSettings.ExtraneousPropertiesOption = (_, _) => Configuration.ExtraneousPropertiesOptions.AutomaticUpdate;
    
    var obj = new Customer { FirstName = "Johan", LastName = "Doe" };

    Assert(obj);

    obj.FirstName = "Bob";
    
    Assert(obj);
    
    Assert(new Customer { FirstName = "Johan", LastName = "Doe" });
  }
}