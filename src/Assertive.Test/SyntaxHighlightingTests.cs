using System;
using System.Collections.Generic;
using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// The assertion source text in failure messages gets C# syntax highlighting
  /// (Configuration.Colors.Expression), like the expression-based pipeline applied through
  /// ExpressionToString: the assertion header, operand sources in expected/actual messages,
  /// and exception-cause messages.
  /// </summary>
  public class SyntaxHighlightingTests : AssertionTestBase
  {
    private const string BrightGreen = "\u001b[92m";
    private const string KeywordColor = "\u001b[1m\u001b[94m";

    private static Exception CaptureWithColors(Action assertion)
    {
      var originalEnabled = Configuration.Colors.Enabled;
      var originalHighlighting = Configuration.Colors.UseSyntaxHighlighting;

      Configuration.Colors.Enabled = true;
      Configuration.Colors.UseSyntaxHighlighting = true;

      try
      {
        return Xunit.Assert.ThrowsAny<Exception>(assertion);
      }
      finally
      {
        Configuration.Colors.Enabled = originalEnabled;
        Configuration.Colors.UseSyntaxHighlighting = originalHighlighting;
      }
    }

    [Fact]
    public void Assertion_text_is_syntax_highlighted()
    {
      var name = "actual";

      var ex = CaptureWithColors(() => Assert(() => name == "expected"));

      // The string literal in the assertion header renders in the string-literal color.
      Xunit.Assert.Contains($"{BrightGreen}\"expected\"", ex.Message);
    }

    [Fact]
    public void Operand_sources_in_the_friendly_message_are_syntax_highlighted()
    {
      var text = "hello";

      var ex = CaptureWithColors(() => Assert(() => text.Contains("xyz")));

      var expected = ((string[])ex.Data["Assertive.Expected"]!)[0];

      // The constant argument keeps its string-literal color inside the expected message.
      Xunit.Assert.Contains($"{BrightGreen}\"xyz\"", expected);
    }

    [Fact]
    public void Exception_cause_messages_are_syntax_highlighted()
    {
      List<string>? list = null;

      var ex = CaptureWithColors(() => Assert(() => list!.Count == 0));

      var handled = ((string[])ex.Data["Assertive.HandledExceptions"]!)[0];

      // Member name and receiver source are highlighted (identifier color varies; assert
      // that highlighting inserted ANSI codes into the cause message at all).
      Xunit.Assert.Contains("\u001b[", handled);
      SnapshotMessage(new Exception(StripAnsi(handled)));
    }

    [Fact]
    public void Bool_pattern_values_are_highlighted_as_keywords()
    {
      var flag = false;

      var ex = CaptureWithColors(() => Assert(() => flag));

      var expected = ((string[])ex.Data["Assertive.Expected"]!)[0];
      var actual = ((string[])ex.Data["Assertive.Actual"]!)[0];

      Xunit.Assert.Contains($"{KeywordColor}true", expected);
      Xunit.Assert.Contains($"{KeywordColor}false", actual);
    }

    [Fact]
    public void Null_pattern_actual_null_is_highlighted_as_a_keyword()
    {
      string? value = null;

      var ex = CaptureWithColors(() => Assert(() => value != null));

      var actual = ((string[])ex.Data["Assertive.Actual"]!)[0];

      Xunit.Assert.Contains($"{KeywordColor}null", actual);
    }

    [Fact]
    public void Highlighting_is_off_when_syntax_highlighting_is_disabled()
    {
      var originalEnabled = Configuration.Colors.Enabled;
      var originalHighlighting = Configuration.Colors.UseSyntaxHighlighting;

      Configuration.Colors.Enabled = true;
      Configuration.Colors.UseSyntaxHighlighting = false;

      try
      {
        var name = "actual";

        var ex = Xunit.Assert.ThrowsAny<Exception>(() => Assert(() => name == "expected"));

        Xunit.Assert.DoesNotContain($"{BrightGreen}\"expected\"", ex.Message);
      }
      finally
      {
        Configuration.Colors.Enabled = originalEnabled;
        Configuration.Colors.UseSyntaxHighlighting = originalHighlighting;
      }
    }
  }
}
