using System;
using System.Collections.Generic;
using System.Text;
using Assertive.Config;

namespace Assertive.Helpers
{
  internal static class StringDiffHelper
  {
    private enum EditKind
    {
      Match,
      Delete,
      Insert
    }

    public static string GetStringDiff(string actual, string expected)
    {
      var colors = Configuration.Colors;

      var expectedLines = SplitLines(expected);
      var actualLines = SplitLines(actual);

      var edits = CalculateEdits(expectedLines, actualLines, StringComparer.Ordinal);

      var sb = new StringBuilder();

      sb.AppendLine();
      sb.AppendLine(colors.DiffHeader("String diff (expected vs actual):"));
      sb.AppendLine(BuildLegend(colors));

      const int contextLines = 2;
      var keep = new bool[edits.Count];
      var hasDiff = false;

      for (var i = 0; i < edits.Count; i++)
      {
        if (edits[i] != EditKind.Match)
        {
          hasDiff = true;
          for (var j = Math.Max(0, i - contextLines); j <= Math.Min(edits.Count - 1, i + contextLines); j++)
          {
            keep[j] = true;
          }
        }
      }

      if (!hasDiff)
      {
        for (var i = 0; i < keep.Length; i++) keep[i] = true;
      }

      var expectedIndex = 0;
      var actualIndex = 0;
      var editIndex = 0;
      var ellipsisText = colors.Enabled ? colors.DiffContext(colors.DiffEllipsis()) : colors.DiffEllipsis();
      var lastWasEllipsis = false;

      while (editIndex < edits.Count)
      {
        if (edits[editIndex] == EditKind.Match)
        {
          if (!keep[editIndex])
          {
            if (!lastWasEllipsis)
            {
              sb.AppendLine($"     {ellipsisText}");
              lastWasEllipsis = true;
            }

            expectedIndex++;
            actualIndex++;
            editIndex++;
            continue;
          }

          var line = expectedLines[expectedIndex];
          var label = FormatMatchLabel(expectedIndex + 1);
          sb.AppendLine($"{label}{FormatContext(line, colors)}");
          expectedIndex++;
          actualIndex++;
          editIndex++;
          lastWasEllipsis = false;
          continue;
        }

        lastWasEllipsis = false;
        var removed = new List<(int LineNumber, string Text)>();
        var added = new List<(int LineNumber, string Text)>();

        while (editIndex < edits.Count && edits[editIndex] != EditKind.Match)
        {
          if (edits[editIndex] == EditKind.Delete)
          {
            removed.Add((expectedIndex + 1, expectedLines[expectedIndex]));
            expectedIndex++;
          }
          else
          {
            added.Add((actualIndex + 1, actualLines[actualIndex]));
            actualIndex++;
          }

          editIndex++;
        }

        var pairCount = Math.Max(removed.Count, added.Count);

        for (var i = 0; i < pairCount; i++)
        {
          var removedLine = i < removed.Count ? removed[i] : ((int LineNumber, string Text)?)null;
          var addedLine = i < added.Count ? added[i] : ((int LineNumber, string Text)?)null;

          if (removedLine != null && addedLine != null)
          {
            var inlineDiff = BuildInlineDiff(removedLine.Value.Text, addedLine.Value.Text, colors);
            sb.AppendLine($"{FormatDiffLabel('-', removedLine.Value.LineNumber, null, colors)}{inlineDiff.Expected}");
            sb.AppendLine($"{FormatDiffLabel('+', null, addedLine.Value.LineNumber, colors)}{inlineDiff.Actual}");
          }
          else if (removedLine != null)
          {
            sb.AppendLine($"{FormatDiffLabel('-', removedLine.Value.LineNumber, null, colors)}{FormatRemoved(removedLine.Value.Text, colors)}");
          }
          else if (addedLine != null)
          {
            sb.AppendLine($"{FormatDiffLabel('+', null, addedLine.Value.LineNumber, colors)}{FormatAdded(addedLine.Value.Text, colors)}");
          }
        }
      }

      return sb.ToString();
    }

    private static (string Expected, string Actual) BuildInlineDiff(string expected, string actual, Configuration.ColorScheme colors)
    {
      var edits = CalculateEdits(expected.ToCharArray(), actual.ToCharArray(), EqualityComparer<char>.Default);
      var colorEnabled = colors.Enabled;

      var segments = new List<(EditKind Kind, string Text)>();
      var expectedIndex = 0;
      var actualIndex = 0;

      void AddSegment(EditKind kind, string text)
      {
        if (segments.Count > 0 && segments[^1].Kind == kind)
        {
          segments[^1] = (kind, segments[^1].Text + text);
        }
        else
        {
          segments.Add((kind, text));
        }
      }

      foreach (var edit in edits)
      {
        switch (edit)
        {
          case EditKind.Match:
            AddSegment(EditKind.Match, EscapeForDisplay(expected[expectedIndex].ToString()));
            expectedIndex++;
            actualIndex++;
            break;
          case EditKind.Delete:
            AddSegment(EditKind.Delete, EscapeForDisplay(expected[expectedIndex].ToString()));
            expectedIndex++;
            break;
          case EditKind.Insert:
            AddSegment(EditKind.Insert, EscapeForDisplay(actual[actualIndex].ToString()));
            actualIndex++;
            break;
        }
      }

      // Trim long unchanged spans to show only local context around differences.
      const int contextLength = 20;
      var ellipsis = "...";
      var firstDiff = segments.FindIndex(s => s.Kind != EditKind.Match);
      var lastDiff = segments.FindLastIndex(s => s.Kind != EditKind.Match);

      if (firstDiff != -1) // only trim when we actually have differences
      {
        for (var i = 0; i < segments.Count; i++)
        {
          if (segments[i].Kind != EditKind.Match) continue;

          var text = segments[i].Text;

          if (i < firstDiff)
          {
            if (text.Length > contextLength)
            {
              segments[i] = (EditKind.Match, ellipsis + text[^contextLength..]);
            }
          }
          else if (i > lastDiff)
          {
            if (text.Length > contextLength)
            {
              segments[i] = (EditKind.Match, text[..contextLength] + ellipsis);
            }
          }
          else
          {
            if (text.Length > contextLength * 2)
            {
              segments[i] = (EditKind.Match, text[..contextLength] + ellipsis + text[^contextLength..]);
            }
          }
        }
      }

      var expectedBuilder = new StringBuilder();
      var actualBuilder = new StringBuilder();

      string? lastExpectedBg = null;
      string? lastActualBg = null;

      void AppendWithBackground(StringBuilder builder, ref string? lastBg, string text, string bg)
      {
        if (text.Length == 0) return;
        if (lastBg != bg)
        {
          builder.Append(bg);
          lastBg = bg;
        }

        builder.Append(text);
      }

      void FinalizeBackground(StringBuilder builder, ref string? lastBg)
      {
        if (lastBg != null)
        {
          builder.Append(colors.ResetCode);
          lastBg = null;
        }
      }

      var expectedBaseBg = colors.DiffRemovedBackgroundCode;
      var actualBaseBg = colors.DiffAddedBackgroundCode;
      var expectedInlineBg = colors.DiffRemovedInlineBackgroundCode;
      var actualInlineBg = colors.DiffAddedInlineBackgroundCode;

      foreach (var segment in segments)
      {
        switch (segment.Kind)
        {
          case EditKind.Match:
            if (colorEnabled)
            {
              AppendWithBackground(expectedBuilder, ref lastExpectedBg, segment.Text, expectedBaseBg);
              AppendWithBackground(actualBuilder, ref lastActualBg, segment.Text, actualBaseBg);
            }
            else
            {
              expectedBuilder.Append(segment.Text);
              actualBuilder.Append(segment.Text);
            }
            break;
          case EditKind.Delete:
            if (colorEnabled)
            {
              AppendWithBackground(expectedBuilder, ref lastExpectedBg, segment.Text, expectedInlineBg);
            }
            else
            {
              expectedBuilder.Append($"[-{segment.Text}-]");
            }
            break;
          case EditKind.Insert:
            if (colorEnabled)
            {
              AppendWithBackground(actualBuilder, ref lastActualBg, segment.Text, actualInlineBg);
            }
            else
            {
              actualBuilder.Append($"[+{segment.Text}+]");
            }
            break;
        }
      }

      FinalizeBackground(expectedBuilder, ref lastExpectedBg);
      FinalizeBackground(actualBuilder, ref lastActualBg);

      return (expectedBuilder.ToString(), actualBuilder.ToString());
    }

    private static List<EditKind> CalculateEdits<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
      var lcs = new int[expected.Count + 1, actual.Count + 1];

      for (var i = 1; i <= expected.Count; i++)
      {
        for (var j = 1; j <= actual.Count; j++)
        {
          if (comparer.Equals(expected[i - 1], actual[j - 1]))
          {
            lcs[i, j] = lcs[i - 1, j - 1] + 1;
          }
          else
          {
            lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
          }
        }
      }

      var edits = new List<EditKind>();

      var x = expected.Count;
      var y = actual.Count;

      while (x > 0 && y > 0)
      {
        if (comparer.Equals(expected[x - 1], actual[y - 1]))
        {
          edits.Add(EditKind.Match);
          x--;
          y--;
        }
        else if (lcs[x - 1, y] > lcs[x, y - 1])
        {
          edits.Add(EditKind.Delete);
          x--;
        }
        else
        {
          edits.Add(EditKind.Insert);
          y--;
        }
      }

      while (x > 0)
      {
        edits.Add(EditKind.Delete);
        x--;
      }

      while (y > 0)
      {
        edits.Add(EditKind.Insert);
        y--;
      }

      edits.Reverse();

      return edits;
    }

    private static List<string> SplitLines(string value)
    {
      var lines = new List<string>();
      if (value.Length == 0)
      {
        lines.Add(string.Empty);
        return lines;
      }

      var builder = new StringBuilder();

      for (var i = 0; i < value.Length; i++)
      {
        var ch = value[i];

        if (ch == '\r')
        {
          builder.Append(ch);

          if (i + 1 < value.Length && value[i + 1] == '\n')
          {
            builder.Append('\n');
            i++;
          }

          lines.Add(builder.ToString());
          builder.Clear();
          continue;
        }

        if (ch == '\n')
        {
          builder.Append(ch);
          lines.Add(builder.ToString());
          builder.Clear();
          continue;
        }

        builder.Append(ch);
      }

      if (builder.Length > 0)
      {
        lines.Add(builder.ToString());
      }

      return lines;
    }

    private static string FormatContext(string value, Configuration.ColorScheme colors)
    {
      var escaped = EscapeForDisplay(value);

      const int maxLength = 80;
      const int contextLength = 38;

      if (escaped.Length > maxLength)
      {
        escaped = escaped[..contextLength] + "..." + escaped[^contextLength..];
      }

      return colors.Enabled ? colors.DiffContext(escaped) : escaped;
    }

    private static string FormatRemoved(string value, Configuration.ColorScheme colors)
    {
      var escaped = EscapeForDisplay(value);
      return colors.Enabled ? colors.DiffRemovedLine(escaped) : $"[-{escaped}-]";
    }

    private static string FormatAdded(string value, Configuration.ColorScheme colors)
    {
      var escaped = EscapeForDisplay(value);
      return colors.Enabled ? colors.DiffAddedLine(escaped) : $"[+{escaped}+]";
    }

    private static string FormatMatchLabel(int lineNumber)
    {
      return $"{lineNumber,4}: ";
    }

    private static string FormatDiffLabel(char marker, int? expectedLine, int? actualLine, Configuration.ColorScheme colors)
    {
      var sb = new StringBuilder();
      sb.Append(marker == ' ' ? "  " : $"{marker} ");

      string FormatLabel(string text, string background) =>
        colors.Enabled ? colors.ApplyBackground(text, background) : text;

      if (expectedLine.HasValue && actualLine.HasValue)
      {
        sb.Append(FormatLabel($"[E{expectedLine}/A{actualLine}]", colors.DiffRemovedBackgroundCode));
      }
      else if (expectedLine.HasValue)
      {
        sb.Append(FormatLabel($"[E{expectedLine}]", colors.DiffRemovedBackgroundCode));
      }
      else if (actualLine.HasValue)
      {
        sb.Append(FormatLabel($"[A{actualLine}]", colors.DiffAddedBackgroundCode));
      }
      else
      {
        sb.Append("[]");
      }

      sb.Append(' ');

      return sb.ToString();
    }

    private static string BuildLegend(Configuration.ColorScheme colors)
    {
      var expectedLabel = colors.Enabled ? colors.Expected("[E#] expected line") : "[E#] expected line";
      var actualLabel = colors.Enabled ? colors.Actual("[A#] actual line") : "[A#] actual line";
      var contextLabel = colors.Enabled ? colors.DiffContext("plain line number = unchanged") : "plain line number = unchanged";
      return $"{(colors.Enabled ? colors.Dimmed("Legend:") : "Legend:")} {expectedLabel}, {actualLabel}, {contextLabel}";
    }

    /// <summary>
    /// Finds the closest matching substring of <paramref name="haystack"/> to <paramref name="needle"/>
    /// using semi-global alignment, and returns a formatted inline diff.
    /// Returns null if no sufficiently close match is found.
    /// </summary>
    public static string? GetClosestSubstringDiff(string haystack, string needle)
    {
      if (needle.Length < 5 || haystack.Length == 0)
      {
        return null;
      }

      // Performance guard: O(m*n) DP
      if ((long)haystack.Length * needle.Length > 5_000_000)
      {
        return null;
      }

      int m = needle.Length;
      int n = haystack.Length;

      // Semi-global alignment: free to start matching anywhere in haystack
      var dp = new int[m + 1, n + 1];

      for (int i = 1; i <= m; i++)
      {
        dp[i, 0] = i;
      }

      // dp[0, j] = 0: starting anywhere in haystack is free

      for (int j = 1; j <= n; j++)
      {
        for (int i = 1; i <= m; i++)
        {
          int cost = needle[i - 1] == haystack[j - 1] ? 0 : 1;
          dp[i, j] = Math.Min(
            dp[i - 1, j - 1] + cost,
            Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1)
          );
        }
      }

      // Find best end position in last row
      int bestDist = m;
      int bestEnd = 0;

      for (int j = 1; j <= n; j++)
      {
        if (dp[m, j] < bestDist)
        {
          bestDist = dp[m, j];
          bestEnd = j;
        }
      }

      // Only show if the match is reasonably close (within 30%)
      if (bestDist == 0 || (double)bestDist / m > 0.3)
      {
        return null;
      }

      // Backtrace to find start position
      int bi = m, bj = bestEnd;

      while (bi > 0 && bj > 0)
      {
        int cost = needle[bi - 1] == haystack[bj - 1] ? 0 : 1;
        if (dp[bi, bj] == dp[bi - 1, bj - 1] + cost)
        {
          bi--;
          bj--;
        }
        else if (dp[bi, bj] == dp[bi - 1, bj] + 1)
        {
          bi--;
        }
        else
        {
          bj--;
        }
      }

      int startPos = bj;
      var matchedSubstring = haystack.Substring(startPos, bestEnd - startPos);

      var colors = Configuration.Colors;

      return colors.Dimmed($"Closest match at position {startPos} ({bestDist} character difference{(bestDist != 1 ? "s" : "")})")
             + "\n" + GetStringDiff(needle, matchedSubstring);
    }

    private static string EscapeForDisplay(string value)
    {
      return value
        .Replace("\\", "\\\\")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");
    }
  }
}
