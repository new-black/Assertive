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

    private static ITestFramework? _activeTestFramework = null;
    private static bool _initialized = false;

    private static ITestFramework? GetActiveTestFramework()
    {
      foreach (var framework in _testFrameworks)
      {
        if (framework.ExceptionType != null)
        {
          return framework;
        }
      }

      return null;
    }

    internal static Exception GetException(string message)
    {
      if (!_initialized)
      {
        _activeTestFramework = GetActiveTestFramework();
        _initialized = true;
      }

      if (_activeTestFramework is { ExceptionType: not null })
      {
        return (Exception)Activator.CreateInstance(_activeTestFramework.ExceptionType, message)!;
      }

      return new AssertiveException(message);
    }
  }
}