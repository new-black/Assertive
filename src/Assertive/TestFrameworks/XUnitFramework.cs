using System;
using System.Reflection;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class XUnitFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    private Type? _enableAssertType = null;
    private MethodInfo? _getCurrentTestMethodInfo = null;
    
    public Type? ExceptionType
    {
      get
      {
        return _exceptionType ??= TestFrameworkHelper.TryGetType("xunit.assert", "Xunit.Sdk.XunitException", "xunit");
      }
    }

    public CurrentTestInfo? GetCurrentTestInfo()
    {
      var attribute = _enableAssertType ??= TestFrameworkHelper.TryGetType("Assertive.xUnit", "Assertive.xUnit.EnableAssertiveSnapshotsAttribute");
      
      if (attribute == null)
      {
        return null;
      }
      
      var method = _getCurrentTestMethodInfo ??= attribute.GetMethod("GetCurrentTestMethod", BindingFlags.Public | BindingFlags.Static);
      
      if (method == null)
      {
        return null;
      }

      dynamic? currentTestMethod = method.Invoke(null, null);
      
      if (currentTestMethod == null)
      {
        return null;
      }
      
      var methodInfo = currentTestMethod.Method as MethodInfo;
      
      if (methodInfo == null || methodInfo.DeclaringType?.FullName == null)
      {
        return null;
      }

      return new CurrentTestInfo()
      {
        Method = methodInfo,
        Name = methodInfo.Name,
        Arguments = [],
        State = currentTestMethod.State,
        ClassName = methodInfo.DeclaringType.FullName
      };
    }
  }
}