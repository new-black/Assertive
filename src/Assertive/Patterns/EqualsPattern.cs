using System;
using System.Linq.Expressions;
using System.Text;
using Assertive.Analyzers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class EqualsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.Expression.NodeType == ExpressionType.Equal
             || EqualityPattern.EqualsMethodShouldBeTrue(failedAssertion.Expression);
    }

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var left = EqualityPattern.GetLeftSide(assertion.Expression);

      var right = left != null ? EqualityPattern.GetRightSide(assertion.Expression, left) : null;

      if (right is { NodeType: ExpressionType.Convert } && right.Type == typeof(object))
      {
        right = ((UnaryExpression)right).Operand;
      }

      object? expected = right != null && IsConstantExpression(right) ? right : right?.ToValue();
      object? actual = left?.ToValue();
      string diff = "";

      // Check if both sides are strings and provide a smart diff
      if (left != null && right != null &&
          left.Type == typeof(string) && right.Type == typeof(string))
      {
        var leftValue = EvaluateExpression(left) as string;
        var rightValue = EvaluateExpression(right) as string;

        if (leftValue != null && rightValue != null && leftValue != rightValue)
        {
          diff = GetStringDiff(leftValue, rightValue);
        }
      }

      return new()
      {
        Expected = $"{right}: {expected}",
        Actual = $"{left}: {actual}{diff}"
      };
    }

    private static string GetStringDiff(string expected, string actual)
    {
      const int contextLength = 20;

      // Find first difference
      int diffIndex = 0;
      int minLength = Math.Min(expected.Length, actual.Length);

      while (diffIndex < minLength && expected[diffIndex] == actual[diffIndex])
      {
        diffIndex++;
      }

      // If strings are identical up to the shorter one's length, the difference is the length
      if (diffIndex == minLength)
      {
        if (expected.Length != actual.Length)
        {
          return $"Strings differ in length: expected {expected.Length} chars, actual {actual.Length} chars";
        }

        // This shouldn't happen since we checked they're not equal, but just in case
        return "";
      }

      // Calculate context window
      int startIndex = Math.Max(0, diffIndex - contextLength);
      int expectedEndIndex = Math.Min(expected.Length, diffIndex + contextLength + 1);
      int actualEndIndex = Math.Min(actual.Length, diffIndex + contextLength + 1);

      // Extract context
      string expectedPrefix = startIndex > 0 ? "..." : "";
      string expectedSuffix = expectedEndIndex < expected.Length ? "..." : "";
      string actualPrefix = startIndex > 0 ? "..." : "";
      string actualSuffix = actualEndIndex < actual.Length ? "..." : "";

      string expectedContext = expected.Substring(startIndex, expectedEndIndex - startIndex);
      string actualContext = actual.Substring(startIndex, actualEndIndex - startIndex);

      // Find the exact difference character in the context
      int diffPosInContext = diffIndex - startIndex;

      // Build the diff message
      var sb = new StringBuilder();
      sb.AppendLine($"Strings differ at index {diffIndex}:");

      // Show expected with diff character highlighted
      sb.Append("  Expected: \"");
      sb.Append(expectedPrefix);
      if (diffPosInContext > 0)
      {
        sb.Append(EscapeString(expectedContext[..diffPosInContext]));
      }

      sb.Append('[');
      if (diffPosInContext < expectedContext.Length)
      {
        sb.Append(EscapeString(expectedContext[diffPosInContext].ToString()));
      }

      sb.Append(']');
      if (diffPosInContext + 1 < expectedContext.Length)
      {
        sb.Append(EscapeString(expectedContext[(diffPosInContext + 1)..]));
      }

      sb.Append(expectedSuffix);
      sb.AppendLine("\"");

      // Show actual with diff character highlighted
      sb.Append("  Actual:   \"");
      sb.Append(actualPrefix);
      if (diffPosInContext > 0)
      {
        sb.Append(EscapeString(actualContext[..diffPosInContext]));
      }

      sb.Append('[');
      if (diffPosInContext < actualContext.Length)
      {
        sb.Append(EscapeString(actualContext[diffPosInContext].ToString()));
      }

      sb.Append(']');
      if (diffPosInContext + 1 < actualContext.Length)
      {
        sb.Append(EscapeString(actualContext[(diffPosInContext + 1)..]));
      }

      sb.Append(actualSuffix);
      sb.Append('"');

      return sb.ToString();
    }

    private static string EscapeString(string s)
    {
      return s.Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}