using System;

namespace Assertive.TestFrameworks
{
  internal interface ITestFramework
  {
    Type? ExceptionType { get; }
  }
}