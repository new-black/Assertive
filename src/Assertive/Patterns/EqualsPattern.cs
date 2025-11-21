using System;
using System.Collections.Generic;
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
        Expected = $"{left}: {expected}",
        Actual = $"{left}: {actual}{diff}"
      };
    }

    private static string GetStringDiff(string expected, string actual)
    {
      const int maxContextSegmentLength = 50;
      var colors = Config.Configuration.Colors;
      var spans = BuildDiffSpans(expected, actual);

      var expectedBuilder = new StringBuilder();
      var actualBuilder = new StringBuilder();

      var expectedIndex = 0;
      var actualIndex = 0;
      var differences = 0;
      int? firstExpectedIndex = null;
      int? firstActualIndex = null;

      for (var i = 0; i < spans.Count; i++)
      {
        var span = spans[i];

        switch (span.Operation)
        {
          case DiffOperation.Equal:
            AppendContext(expectedBuilder, actualBuilder, span.Text, colors, maxContextSegmentLength);
            expectedIndex += span.Text.Length;
            actualIndex += span.Text.Length;
            break;
          case DiffOperation.Delete:
            differences += span.Text.Length;
            firstExpectedIndex ??= expectedIndex;
            firstActualIndex ??= actualIndex;
            var escapedDeleted = EscapeString(span.Text);
            var deletedPlaceholder = new string('∅', escapedDeleted.Length);
            expectedBuilder.Append(colors.DiffExpectedChar(escapedDeleted));
            actualBuilder.Append(colors.DiffActualChar(deletedPlaceholder));
            expectedIndex += span.Text.Length;
            break;
          case DiffOperation.Insert:
            differences += span.Text.Length;
            firstExpectedIndex ??= expectedIndex;
            firstActualIndex ??= actualIndex;
            var escapedInserted = EscapeString(span.Text);
            var insertedPlaceholder = new string('∅', escapedInserted.Length);
            expectedBuilder.Append(colors.DiffExpectedChar(insertedPlaceholder));
            actualBuilder.Append(colors.DiffActualChar(escapedInserted));
            actualIndex += span.Text.Length;
            break;
        }
      }

      var sb = new StringBuilder();
      sb.AppendLine();
      sb.AppendLine();
      var differenceSummary = differences == 1 ? "1 edit" : $"{differences} edits";
      sb.AppendLine(colors.DiffHeader($"Strings differ ({differenceSummary}; expected length {colors.Highlight(expected.Length.ToString())}, actual length {colors.Highlight(actual.Length.ToString())})"));
      if (firstExpectedIndex.HasValue && firstActualIndex.HasValue)
      {
        sb.AppendLine(colors.DiffHeader($"First difference at expected index {colors.Highlight(firstExpectedIndex.Value.ToString())}, actual index {colors.Highlight(firstActualIndex.Value.ToString())}."));
      }
      sb.AppendLine();
      sb.Append(colors.DiffExpectedLabel());
      sb.Append('"');
      sb.Append(expectedBuilder);
      sb.AppendLine("\"");
      sb.Append(colors.DiffActualLabel());
      sb.Append('"');
      sb.Append(actualBuilder);
      sb.Append('"');

      return sb.ToString();
    }

    private static void AppendContext(StringBuilder expectedBuilder, StringBuilder actualBuilder, string text, Config.Configuration.ColorScheme colors, int maxContextSegmentLength)
    {
      var escaped = EscapeString(text);

      if (escaped.Length <= maxContextSegmentLength)
      {
        expectedBuilder.Append(colors.DiffContext(escaped));
        actualBuilder.Append(colors.DiffContext(escaped));
        return;
      }

      var headLength = maxContextSegmentLength / 2;
      var tailLength = maxContextSegmentLength - headLength;
      var head = escaped[..headLength];
      var tail = escaped[^tailLength..];

      expectedBuilder.Append(colors.DiffContext(head));
      expectedBuilder.Append(colors.DiffEllipsis());
      expectedBuilder.Append(colors.DiffContext(tail));

      actualBuilder.Append(colors.DiffContext(head));
      actualBuilder.Append(colors.DiffEllipsis());
      actualBuilder.Append(colors.DiffContext(tail));
    }

    private static List<DiffSpan> BuildDiffSpans(string expected, string actual)
    {
      var lcs = new int[expected.Length + 1, actual.Length + 1];

      for (var i = 1; i <= expected.Length; i++)
      {
        for (var j = 1; j <= actual.Length; j++)
        {
          if (expected[i - 1] == actual[j - 1])
          {
            lcs[i, j] = lcs[i - 1, j - 1] + 1;
          }
          else
          {
            lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
          }
        }
      }

      var reversed = new List<DiffSpan>();
      var x = expected.Length;
      var y = actual.Length;

      while (x > 0 || y > 0)
      {
        if (x > 0 && y > 0 && expected[x - 1] == actual[y - 1])
        {
          reversed.Add(new DiffSpan(DiffOperation.Equal, expected[--x].ToString()));
          y--;
        }
        else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
        {
          reversed.Add(new DiffSpan(DiffOperation.Insert, actual[--y].ToString()));
        }
        else
        {
          reversed.Add(new DiffSpan(DiffOperation.Delete, expected[--x].ToString()));
        }
      }

      reversed.Reverse();
      return CombineAdjacentSpans(reversed);
    }

    private static List<DiffSpan> CombineAdjacentSpans(List<DiffSpan> spans)
    {
      if (spans.Count == 0) return spans;

      var merged = new List<DiffSpan>();
      var current = spans[0];

      for (var i = 1; i < spans.Count; i++)
      {
        var span = spans[i];
        if (span.Operation == current.Operation)
        {
          current = new DiffSpan(current.Operation, current.Text + span.Text);
        }
        else
        {
          merged.Add(current);
          current = span;
        }
      }

      merged.Add(current);
      return merged;
    }

    private enum DiffOperation
    {
      Equal,
      Insert,
      Delete
    }

    private readonly record struct DiffSpan(DiffOperation Operation, string Text);

    private static string EscapeString(string s)
    {
      return s.Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}
