using System;

namespace Assertive.Frameworks
{
  internal class MSTestFramework : ITestFramework
  {
    public bool IsAvailable
    {
      get
      {
        ExceptionType = TestFrameworkHelper.TryGetExceptionType("Microsoft.VisualStudio.TestPlatform.TestFramework", "Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException");

        return ExceptionType != null;
      }
    }

    public Type? ExceptionType { get; private set; } = null;
  }
}