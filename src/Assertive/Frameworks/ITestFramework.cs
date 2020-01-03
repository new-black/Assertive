using System;

namespace Assertive.Frameworks
{
  public interface ITestFramework
  {
    bool IsAvailable { get; }
    Type ExceptionType { get; }
  }
}