using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Assertive.ExceptionPatterns;

namespace Assertive
{
  internal class FailedAssertionExceptionProvider
  {
    private readonly Expression<Func<bool>> _assertion;

    public FailedAssertionExceptionProvider(Expression<Func<bool>> assertion)
    {
      _assertion = assertion;
    }

    private string FormatExceptionMessage(FailedAnalyzedAssertion failedAssertion, Exception originalException)
    {
      var assertionExpression = ExpressionStringBuilder.ExpressionToString(failedAssertion.Assertion.Expression);

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

      if (originalException != null)
      {
        if (failedAssertion.CauseOfException != null)
        {
          result += $@"

Cause of exception: {ExpressionStringBuilder.ExpressionToString(failedAssertion.CauseOfException)}";
        }
        
        result += $@"

Exception: {originalException.Message}

StackTrace: {originalException.StackTrace}";
      }

      result += Environment.NewLine;

      return result;
    }

    internal Exception GetException(Exception assertionException = null)
    {
      var failureAnalyzer = new AssertionFailureAnalyzer(_assertion, assertionException);
      
      var failedAssertions = failureAnalyzer.AnalyzeAssertionFailures();

      var message = string.Join(Environment.NewLine + Environment.NewLine, failedAssertions.Select(f => FormatExceptionMessage(f, assertionException)));

      return ExceptionHelper.GetException(message);
    }
  }
}