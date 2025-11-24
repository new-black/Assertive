using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
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
      var colors = Config.Configuration.Colors;
      var assertionExpression = ExpressionToString(failedAssertion.Assertion.Expression);

      var result = new List<string>();

      if (failedAssertion.Message == null)
      {
        result.Add($"""
                  
                  {assertionExpression}
                  """);
      }
      else
      {
        result.Add($"""
                  
                  {assertionExpression}
                  
                  {failedAssertion.Message}
                  """);
      }

      if (_assertion.Message != null)
      {
        var messageContent = _assertion.Message is string s ? s : Serializer.Serialize(_assertion.Message).ToString();
        result.Add($"""
                   {colors.MetadataHeader("MESSAGE")}
                   {colors.Highlight(messageContent)}
                   """);
      }

      if (_assertion.Context != null)
      {
        var contextExpr = ExpressionToString(_assertion.Context.Body);
        var contextValue = Serializer.Serialize(EvaluateExpression(_assertion.Context.Body));
        result.Add($"""
                   {colors.MetadataHeader("CONTEXT")}
                   {contextExpr} = {contextValue}
                   """);
      }

      if (originalException != null)
      {
        if (failedAssertion.HandledException?.CauseOfException != null)
        {
          var causeExpr = ExpressionToString(failedAssertion.HandledException.CauseOfException);
          result.Add($"""
                     {colors.MetadataHeader("CAUSE OF EXCEPTION")}
                     {causeExpr}
                     """);
        }

        result.Add($"""
                   {colors.MetadataHeader("EXCEPTION")}
                   {colors.Actual(originalException.Message)}
                   {colors.MetadataHeader("STACKTRACE")}
                   {colors.Dimmed(originalException.StackTrace ?? "")}
                   """);
      }

      var locals = LocalsProvider.GetLocals(failedAssertion.Assertion.Expression, context.EvaluatedExpressions);

      if (locals != null)
      {
        result.Add($"""
                   {colors.MetadataHeader("LOCALS")}
                   {FormatLocals(locals, colors)}
                   """);
      }

      result.Add(colors.Dimmed(new string('·', 80)));
      
      return string.Join(Environment.NewLine, result) + Environment.NewLine;
    }

    private static string FormatLocals(System.Collections.Generic.List<LocalVariable> locals, Configuration.ColorScheme colors)
    {
      var lines = new System.Collections.Generic.List<string>();

      foreach (var local in locals)
      {
        lines.Add($"{colors.LocalName(local.Name)} = {colors.LocalValue(local.Value)}");
      }

      return string.Join(Environment.NewLine, lines);
    }

    internal Exception GetException(Exception? assertionException = null)
    {
      var context = new AssertionFailureContext(_assertion, assertionException);
      
      var failureAnalyzer = new AssertionFailureAnalyzer(context);
      
      var failedAssertions = failureAnalyzer.AnalyzeAssertionFailures();

      var message = string.Join(Environment.NewLine + Environment.NewLine, failedAssertions.Select(f => FormatExceptionMessage(context, f, assertionException)));
      
      var exception = ExceptionHelper.GetException(message);

      var expectedData = new List<string>();
      var actualData = new List<string>();
      var handledExceptionData = new List<string>();

      foreach (var failedAssertion in failedAssertions)
      {
        if (failedAssertion.ExpectedAndActual?.Expected is {} expected)
        {
          expectedData.Add(expected.ToString());
        }
        if (failedAssertion.ExpectedAndActual?.Actual is {} actual)
        {
          actualData.Add(actual.ToString());
        }

        if (failedAssertion.HandledException != null)
        {
          handledExceptionData.Add(failedAssertion.HandledException.Message?.ToString() ?? "");
        }
      }

      exception.Data["Assertive.Expected"] = expectedData.ToArray();
      exception.Data["Assertive.Actual"] = actualData.ToArray();
      exception.Data["Assertive.HandledExceptions"] = handledExceptionData.ToArray();

      return exception;
    }
  }
}