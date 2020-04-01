using System;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class NUnitTestFramework : ITestFramework
  {
    public bool IsAvailable
    {
      get
      {
        ExceptionType = TestFrameworkHelper.TryGetExceptionType("nunit.framework", "NUnit.Framework.AssertionException");

        return ExceptionType != null;
      }
    }

    public Type? ExceptionType { get; private set; } = null;
  }
}