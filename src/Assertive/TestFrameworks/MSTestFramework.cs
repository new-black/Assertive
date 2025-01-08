using System;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class MSTestFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    
    public Type? ExceptionType
    {
      get
      {
        return _exceptionType ??= TestFrameworkHelper.TryGetType("Microsoft.VisualStudio.TestPlatform.TestFramework", "Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException");
      }
    }

    public CurrentTestInfo? GetCurrentTestInfo()
    {
      throw new NotImplementedException();
    }
  }
}