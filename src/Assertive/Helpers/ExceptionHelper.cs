using System;
using Assertive.TestFrameworks;

namespace Assertive.Helpers
{
  internal static class ExceptionHelper
  {
    private class AssertiveException : Exception
    {
      public AssertiveException(string message) : base(message){}
    }

    private static readonly ITestFramework[] _testFrameworks =
    [
      new XUnitFramework(),
      new MSTestFramework(), 
      new NUnitTestFramework()
    ];

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Constructs the active test framework's exception type. That type lives in the test framework assembly the running tests reference, so its (message) constructor is present; when no framework is detected this falls back to AssertiveException.")]
    internal static Exception GetException(string message)
    {
      var activeTestFramework = ITestFramework.GetActiveTestFramework();

      if (activeTestFramework is { ExceptionType: not null })
      {
        return (Exception)Activator.CreateInstance(activeTestFramework.ExceptionType, message)!;
      }

      return new AssertiveException(message);
    }
  }
}