using System;
using Assertive.ExceptionPatterns;
using Assertive.Interfaces;

namespace Assertive.Analyzers
{
  internal class FriendlyMessageProviderForException
  {
    private readonly AssertionFailureContext _context;

    private readonly IExceptionHandlerPattern[] _patterns =
    [
      new NullReferencePattern(),
      new ArgumentNullParamSourcePattern(),
      new LinqElementCountPattern(),
      new IndexOutOfRangeExceptionPattern()
    ];

    public FriendlyMessageProviderForException(AssertionFailureContext context)
    {
      _context = context;
    }

    public FailedAnalyzedAssertion AnalyzeException(FailedAssertion part)
    {
      HandledException? handledException = null;
      IExceptionHandlerPattern? handledExceptionPattern = null;
      var exception = part.Exception!;

      foreach (var pattern in _patterns)
      {
        if (pattern.IsMatch(exception))
        {
          handledException = pattern.Handle(part);

          if (handledException != null)
          {
            handledExceptionPattern = pattern;
            break;
          }
        }
      }

      FormattableString? failedAssertionMessage = handledException?.Message;
      
      if (handledException == null)
      {
        failedAssertionMessage = $@"Assertion threw {exception.GetType().FullName}: {exception.Message}";
      }

      return new FailedAnalyzedAssertion(part, FriendlyMessageFormatter.GetString(failedAssertionMessage, _context.EvaluatedExpressions), 
        handledExceptionPattern, 
        handledException);
    }
  }
}