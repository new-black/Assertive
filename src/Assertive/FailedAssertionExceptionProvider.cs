using System;
using System.Linq;
using static Assertive.ExpressionHelper;

namespace Assertive
{
  internal class FailedAssertionExceptionProvider
  {
    private readonly Assertion _assertion;

    public FailedAssertionExceptionProvider(Assertion assertion)
    {
      _assertion = assertion;
    }

    private string FormatExceptionMessage(FailedAnalyzedAssertion failedAssertion, Exception? originalException)
    {
      var assertionExpression = ExpressionToString(failedAssertion.Assertion.Expression);

      string result;
      
      if (failedAssertion.Message == null)
      {
        result = $"Assertion failed: {assertionExpression}";
      }
      else
      {
        result = $@"{failedAssertion.Message}

Assertion: {assertionExpression}";
      }

      if (_assertion.Message != null)
      {
        result += $@"

Message: {_assertion.Message}";
      }

      if (_assertion.Context != null)
      {
        result += $@"

Context: {ExpressionToString(_assertion.Context.Body)} = {Serializer.Serialize(EvaluateExpression(_assertion.Context.Body), 0, null)}
";
      }

      if (originalException != null)
      {
        if (failedAssertion.CauseOfException != null)
        {
          result += $@"

Cause of exception: {ExpressionToString(failedAssertion.CauseOfException)}";
        }
        
        result += $@"

Exception: {originalException.Message}

StackTrace: {originalException.StackTrace}";
      }

      result += Environment.NewLine;

      return result;
    }

    internal Exception GetException(Exception? assertionException = null)
    {
      var failureAnalyzer = new AssertionFailureAnalyzer(_assertion.Expression, assertionException);
      
      var failedAssertions = failureAnalyzer.AnalyzeAssertionFailures();

      var message = string.Join(Environment.NewLine + Environment.NewLine, failedAssertions.Select(f => FormatExceptionMessage(f, assertionException)));

      return ExceptionHelper.GetException(message);
    }
  }
}