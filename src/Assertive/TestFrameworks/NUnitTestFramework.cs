using System;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class NUnitTestFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    
    public Type? ExceptionType
    {
      get
      {
        return _exceptionType ??= TestFrameworkHelper.TryGetExceptionType("nunit.framework", "NUnit.Framework.AssertionException");
      }
    }
  }
}