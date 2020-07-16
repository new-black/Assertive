using System;
using System.Linq;
using Assertive.Helpers;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Analyzers
{
  internal class FailedAssertionExceptionProvider
  {
    private readonly Assertion _assertion;

    public FailedAssertionExceptionProvider(Assertion assertion)
    {
      _assertion = assertion;
    }

    private string FormatExceptionMessage(AssertionFailureContext context, FailedAnalyzedAssertion failedAssertion, Exception? originalException)
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

Message: {(_assertion.Message is string s ? s : Serializer.Serialize(_assertion.Message))}";
      }

      if (_assertion.Context != null)
      {
        result += $@"

Context: {ExpressionToString(_assertion.Context.Body)} = {Serializer.Serialize(EvaluateExpression(_assertion.Context.Body))}
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

      var locals = LocalsProvider.LocalsToString(failedAssertion.Assertion.Expression, context.EvaluatedExpressions);

      if (locals != null)
      {
        result += $@"

Locals:

{locals}
";
      }

      result += Environment.NewLine;

      return result;
    }

    internal Exception GetException(Exception? assertionException = null)
    {
      var context = new AssertionFailureContext(_assertion, assertionException);
      
      var failureAnalyzer = new AssertionFailureAnalyzer(context);
      
      var failedAssertions = failureAnalyzer.AnalyzeAssertionFailures();

      var message = string.Join(Environment.NewLine + Environment.NewLine, failedAssertions.Select(f => FormatExceptionMessage(context, f, assertionException)));

      return ExceptionHelper.GetException(message);
    }
  }
}