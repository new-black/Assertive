using System;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class NUnitTestFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    private Type? _testContextType = null;
    
    public Type? ExceptionType
    {
      get
      {
        return _exceptionType ??= TestFrameworkHelper.TryGetType("nunit.framework", "NUnit.Framework.AssertionException");
      }
    }

    public CurrentTestInfo? GetCurrentTestInfo()
    {
      var testContextType = _testContextType ??= TestFrameworkHelper.TryGetType("nunit.framework", "NUnit.Framework.Internal.TestExecutionContext");

      dynamic? currentTest = testContextType?.GetProperty("CurrentContext")?.GetValue(null);
      
      if (currentTest == null)
      {
        return null;
      }
      
      var test = currentTest.CurrentTest;

      return new CurrentTestInfo()
      {
        Name = test.Name,
        ClassName = test.ClassName,
        Arguments = test.Arguments,
        State = test
      };
    }
  }
}