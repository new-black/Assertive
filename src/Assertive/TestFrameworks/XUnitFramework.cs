using System;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class XUnitFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    
    public Type? ExceptionType
    {
      get
      {
        return _exceptionType ??= TestFrameworkHelper.TryGetExceptionType("xunit.assert", "Xunit.Sdk.XunitException", "xunit");
      }
    }
  }
}