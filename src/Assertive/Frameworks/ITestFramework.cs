using System;

namespace Assertive.Frameworks
{
  internal interface ITestFramework
  {
    bool IsAvailable { get; }
    Type? ExceptionType { get; }
  }
}