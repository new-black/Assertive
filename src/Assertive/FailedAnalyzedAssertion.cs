using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Assertive
{
  internal class FailedAnalyzedAssertion
  {
    internal FailedAnalyzedAssertion(FailedAssertion part, string message, IFriendlyMessagePattern? friendlyMessagePattern)
    {
      Assertion = part;
      Message = message;
      FriendlyMessagePattern = friendlyMessagePattern;
    }
    
    internal FailedAnalyzedAssertion(FailedAssertion part, string message, IExceptionHandlerPattern? exceptionHandlerPattern, Expression causeOfException)
    {
      Assertion = part;
      Message = message;
      ExceptionHandlerPattern = exceptionHandlerPattern;
      CauseOfException = causeOfException;
    }
    
    public FailedAssertion Assertion { get; }
    public string Message { get; }
    public IExceptionHandlerPattern ExceptionHandlerPattern { get; }
    public Expression CauseOfException { get; }
    public IFriendlyMessagePattern? FriendlyMessagePattern { get; }
  }
}
