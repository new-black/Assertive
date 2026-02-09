using System;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Assertive.Analyzers;
using Assertive.Config;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class ContainsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsContainsMethodCall(failedAssertion.ExpressionWithoutNegation);
    }

    private static bool IsContainsMethodCall(Expression expression)
    {
      return expression is MethodCallExpression { Method.Name: nameof(Enumerable.Contains) };
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var notContains = assertion.IsNegated;

      var callExpression = (MethodCallExpression)assertion.ExpressionWithoutNegation;

      var instance = callExpression.Object;

      var isExtensionMethod = callExpression.Method.IsDefined(typeof(ExtensionAttribute), false);

      if (isExtensionMethod)
      {
        instance = callExpression.Arguments.First();
      }

      var expectedContainedValueExpression = callExpression.Arguments.Skip(isExtensionMethod ? 1 : 0).First();

      FormattableString expectedValueString;

      if (ExpressionHelper.IsConstantExpression(expectedContainedValueExpression))
      {
        expectedValueString = $"{expectedContainedValueExpression}";
      }
      else
      {
        expectedValueString = $"{expectedContainedValueExpression} (value: {expectedContainedValueExpression.ToValue()})";
      }

      if (instance != null && instance.Type == typeof(string))
      {
        var hint = !notContains ? GetStringContainsHint(instance, expectedContainedValueExpression) : "";

        return new ExpectedAndActual()
        {
          Expected = $"{instance} should{(notContains ? " not " : " ")}contain the substring {expectedValueString}.",
          Actual = $"{instance}: {instance.ToValue()}{hint}"
        };
      }

      return new ExpectedAndActual()
      {
        Expected = $"{instance} should{(notContains ? " not " : " ")}contain {expectedValueString}.",
        Actual = $"{instance}: {instance?.ToValue()}"
      };
    }

    private static string GetStringContainsHint(Expression instanceExpression, Expression expectedExpression)
    {
      try
      {
        var actualValue = ExpressionHelper.EvaluateExpression(instanceExpression) as string;
        var expectedValue = ExpressionHelper.EvaluateExpression(expectedExpression) as string;

        if (actualValue == null || expectedValue == null)
          return "";

        var colors = Configuration.Colors;
        var hints = new System.Collections.Generic.List<string>();

        // Check for newline type mismatch
        var normalizedActual = actualValue.Replace("\r\n", "\n").Replace("\r", "\n");
        var normalizedExpected = expectedValue.Replace("\r\n", "\n").Replace("\r", "\n");

        if (normalizedActual.Contains(normalizedExpected))
        {
          hints.Add(colors.Dimmed("The strings differ only in line endings"));
          hints.Add(StringDiffHelper.GetStringDiff(expectedValue, actualValue));
        }
        // Check for case mismatch
        else if (actualValue.Contains(expectedValue, StringComparison.OrdinalIgnoreCase))
        {
          hints.Add(colors.Dimmed("The strings differ only in casing"));
        }
        else
        {
          // Find the closest matching substring and show an inline diff
          var closestMatch = StringDiffHelper.GetClosestSubstringDiff(actualValue, expectedValue);
          if (closestMatch != null)
          {
            hints.Add(closestMatch);
          }
        }

        if (hints.Count > 0)
        {
          return "\n" + string.Join("\n", hints);
        }
      }
      catch
      {
        // Don't let hint generation break the assertion message
      }

      return "";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}