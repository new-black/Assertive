using Assertive.TestFrameworks;
using static Assertive.DSL;

namespace Assertive.Test.NUnit;

public class CurrentTestInfoTests
{
  [Test]
  public void Can_get_current_test_info()
  {
    var testFramework = new NUnitTestFramework();
    var currentTestInfo = testFramework.GetCurrentTestInfo();

    Assert(() => currentTestInfo.Name == nameof(Can_get_current_test_info)
                 && currentTestInfo.Name == TestContext.CurrentContext.Test.Name
                 && currentTestInfo.ClassName == TestContext.CurrentContext.Test.ClassName
                 && currentTestInfo.Arguments.Length == 0);
  }
  
  [TestCase("arg1", "arg2", 3, 4.0)]
  [TestCase(null, "", 3, 4.0)]
  public void Can_get_current_test_info_with_arguments(string? a, string b, int c, double d)
  {
    var testFramework = new NUnitTestFramework();
    var currentTestInfo = testFramework.GetCurrentTestInfo();

    Assert(() => currentTestInfo.Name.StartsWith("""Can_get_current_test_info_with_arguments(""")
                 && currentTestInfo.Name == TestContext.CurrentContext.Test.Name
                 && currentTestInfo.ClassName == TestContext.CurrentContext.Test.ClassName
                 && (string)currentTestInfo.Arguments[0] == a
                 && (string)currentTestInfo.Arguments[1] == b
                 && (int)currentTestInfo.Arguments[2] == c
                 && (double)currentTestInfo.Arguments[3] == d);
  }
}