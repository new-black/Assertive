using System;
using System.Linq.Expressions;
using Assertive.Analyzers;

namespace Assertive.Interfaces
{
  internal interface IExceptionHandlerPattern
  {
    bool IsMatch(Exception exception);
    HandledException? Handle(FailedAssertion assertion);
  }

  internal class HandledException
  {
    public HandledException(FormattableString message, Expression causeOfException)
    {
      Message = message;
      CauseOfException = causeOfException;
    }
    
    public FormattableString Message { get; }
    public Expression CauseOfException { get; }
  }
}