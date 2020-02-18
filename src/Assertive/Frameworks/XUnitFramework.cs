using System;

namespace Assertive.Frameworks
{
  internal class XUnitFramework : ITestFramework
  {
    public bool IsAvailable
    {
      get
      {
        ExceptionType = TestFrameworkHelper.TryGetExceptionType("xunit.assert", "Xunit.Sdk.XunitException");

        return ExceptionType != null;
      }
    }

    public Type? ExceptionType { get; private set; } = null;
  }
}