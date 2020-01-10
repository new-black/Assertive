using System;
using Assertive.ExceptionPatterns;

namespace Assertive
{
  internal class FriendlyMessageProviderForException
  {
    private readonly IExceptionHandlerPattern[] _patterns = 
    {
      new NullReferencePattern(),
      new ArgumentNullParamSourcePattern(),
      new LinqElementCountPattern(),
      new IndexOutOfRangeExceptionPattern(), 
    };
    
    public FailedAnalyzedAssertion AnalyzeException(FailedAssertion part)
    {
      HandledException handledException = null;
      IExceptionHandlerPattern handledExceptionPattern = null;

      foreach (var pattern in _patterns)
      {
        if (pattern.IsMatch(part.Exception))
        {
          handledException = pattern.Handle(part);

          if (handledException != null)
          {
            handledExceptionPattern = pattern;
            break;
          }
        }
      }

      FormattableString failedAssertionMessage = handledException?.Message;
      
      if (handledException == null)
      {
        failedAssertionMessage = $@"Assertion threw {part.Exception.GetType().FullName}: {part.Exception.Message}";
      }

      return new FailedAnalyzedAssertion(part, FriendlyMessageFormatter.GetString(failedAssertionMessage), 
        handledExceptionPattern, 
        handledException?.CauseOfException);
    }
  }
}