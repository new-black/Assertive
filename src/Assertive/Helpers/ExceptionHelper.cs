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