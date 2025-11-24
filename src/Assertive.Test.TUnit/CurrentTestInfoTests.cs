using System.Reflection;
using Assertive.TestFrameworks;
using TUnit.Core;

namespace Assertive.Test.TUnit;

public class CurrentTestInfoTests
{
  [Test]
  public void Can_get_current_test_info()
  {
    var testFramework = new TUnitFramework();
    
    var currentTestInfo = testFramework.GetCurrentTestInfo();

    var method = GetType().GetMethod(nameof(Can_get_current_test_info), BindingFlags.Instance | BindingFlags.Public);

    Assertive.Assert.That(() => currentTestInfo != null);
    Assertive.Assert.That(() => currentTestInfo!.Name == nameof(Can_get_current_test_info));
    Assertive.Assert.That(() => currentTestInfo.Method == method);
    Assertive.Assert.That(() => currentTestInfo.ClassName == GetType().FullName);
    Assertive.Assert.That(() => currentTestInfo.Arguments.Length == 0);
  }
}
