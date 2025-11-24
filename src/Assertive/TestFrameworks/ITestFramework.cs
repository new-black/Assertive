using System;

namespace Assertive.TestFrameworks
{
  internal interface ITestFramework
  {
    Type? ExceptionType { get; }
    CurrentTestInfo? GetCurrentTestInfo();
    
    
    private static readonly ITestFramework[] _testFrameworks =
    [
      new XUnitFramework(),
      new MSTestFramework(), 
      new NUnitTestFramework(),
      new TUnitFramework()
    ];

    private static ITestFramework? _activeTestFramework = null;
    private static bool _initialized = false;

    internal static ITestFramework? GetActiveTestFramework()
    {
      if (!_initialized)
      {
        _activeTestFramework = GetActiveTestFrameworkImpl();
        _initialized = true;
      }

      return _activeTestFramework;

      ITestFramework? GetActiveTestFrameworkImpl()
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
    }

  }
}
