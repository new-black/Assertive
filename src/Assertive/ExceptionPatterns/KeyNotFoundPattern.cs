using System;
using System.Collections.Generic;
using Assertive.Analyzers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class KeyNotFoundPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is KeyNotFoundException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      throw new NotImplementedException();
    }
  }
}