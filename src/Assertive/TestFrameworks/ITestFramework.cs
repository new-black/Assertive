using System;

namespace Assertive.TestFrameworks
{
  internal interface ITestFramework
  {
    bool IsAvailable { get; }
    Type? ExceptionType { get; }
  }
}