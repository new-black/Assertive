using System;
using System.Collections.Generic;
using Assertive.Helpers;

namespace Assertive.Config
{
  public static partial class Configuration
  {
    /// <summary>
    /// Centralized colorization configuration for assertion failure messages.
    /// Set <see cref="Enabled"/> to false to disable all colorization.
    /// </summary>
    public class ColorScheme
    {
      private static HashSet<string> _keywords = [                                                                                          
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",   
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",  
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",      
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", 
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",   
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",        
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",      
        "virtual", "void", "volatile", "while", "var", "dynamic", "async", "await", "nameof",    
        "when", "where", "yield", "select", "from", "let", "orderby", "group", "by", "into",     
        "join", "on", "equals", "ascending", "descending"                                        
      ];                                                                                         

      /// <summary>
      /// Enables or disables all colorization. When false, all color methods return plain text.
      /// </summary>
      public bool Enabled { get; set; }
      
      /// <summary>
      /// Enables or disables C# syntax highlighting in expression output. Defaults to true.
      /// Only applies when <see cref="Enabled"/> is also true.
      /// </summary>
      public bool UseSyntaxHighlighting { get; set; } = true;

      public ColorScheme()
      {
        Enabled = ShouldEnableColors();
      }

      // ANSI escape codes
      private const string Reset = "\u001b[0m";
      private const string Bold = "\u001b[1m";
      private const string Dim = "\u001b[2m";
      private const string Italic = "\u001b[3m";
      private const string Underline = "\u001b[4m";

      // Foreground colors
      private const string Black = "\u001b[30m";
      private const string Red = "\u001b[31m";
      private const string Green = "\u001b[32m";
      private const string Yellow = "\u001b[33m";
      private const string Blue = "\u001b[34m";
      private const string Magenta = "\u001b[35m";
      private const string Cyan = "\u001b[36m";
      private const string White = "\u001b[37m";

      // Bright foreground colors
      private const string BrightBlack = "\u001b[90m";
      private const string BrightRed = "\u001b[91m";
      private const string BrightGreen = "\u001b[92m";
      private const string BrightYellow = "\u001b[93m";
      private const string BrightBlue = "\u001b[94m";
      private const string BrightMagenta = "\u001b[95m";
      private const string BrightCyan = "\u001b[96m";
      private const string BrightWhite = "\u001b[97m";

      // Background colors
      private const string BgBlack = "\u001b[40m";
      private const string BgRed = "\u001b[41m";
      private const string BgGreen = "\u001b[42m";
      private const string BgYellow = "\u001b[43m";
      private const string BgBlue = "\u001b[44m";
      private const string BgMagenta = "\u001b[45m";
      private const string BgCyan = "\u001b[46m";
      private const string BgWhite = "\u001b[47m";

      // Bright background colors
      private const string BgBrightBlack = "\u001b[100m";
      private const string BgBrightRed = "\u001b[101m";
      private const string BgBrightGreen = "\u001b[102m";
      private const string BgBrightYellow = "\u001b[103m";
      private const string BgBrightBlue = "\u001b[104m";
      private const string BgBrightMagenta = "\u001b[105m";
      private const string BgBrightCyan = "\u001b[106m";
      private const string BgBrightWhite = "\u001b[107m";

      /// <summary>
      /// Applies color formatting to text if colorization is enabled.
      /// </summary>
      private string ApplyColor(string text, string colorCodes)
      {
        if (!Enabled)
        {
          return text;
        }

        return $"{colorCodes}{text}{Reset}";
      }

      /// <summary>
      /// Normalizes line endings to \r\n for consistent rendering across environments.
      /// </summary>
      internal static string NormalizeLineEndings(string text)
      {
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
      }


      /// <summary>
      /// Creates the "EXPECTED" header with fancy styling.
      /// </summary>
      internal string ExpectedHeader()
      {
        if (!Enabled)
        {
          return "[EXPECTED]";
        }

        // Green background with black text, bold
        var header = ApplyColor(" ✓ EXPECTED".PadRight(80, ' '), $"{Bold}{Black}{BgBrightGreen}");
        return $"{header}";
      }

      /// <summary>
      /// Creates the "ACTUAL" header with fancy styling.
      /// </summary>
      internal string ActualHeader()
      {
        if (!Enabled)
        {
          return "[ACTUAL]";
        }

        // Red background with white text, bold
        var header = ApplyColor(" ✗ ACTUAL".PadRight(80, ' '), $"{Bold}{BrightWhite}{BgBrightRed}");
        return header;
      }

      /// <summary>
      /// Highlights the expected value.
      /// </summary>
      internal string Expected(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Green}");
      }

      /// <summary>
      /// Highlights the actual value.
      /// </summary>
      internal string Actual(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Red}");
      }

      /// <summary>
      /// Highlights a difference or error.
      /// </summary>
      internal string Diff(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Bold}{BrightYellow}{BgBrightBlack}");
      }

      /// <summary>
      /// Highlights important information.
      /// </summary>
      internal string Highlight(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Bold}{BrightCyan}");
      }

      /// <summary>
      /// Dims less important text.
      /// </summary>
      internal string Dimmed(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, Dim);
      }

      /// <summary>
      /// Formats a string diff section header.
      /// </summary>
      internal string DiffHeader(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        var header = ApplyColor(text, $"{Bold}{BrightYellow}");
        return header;
      }

      /// <summary>
      /// Highlights matching context in diff.
      /// </summary>
      internal string DiffContext(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Dim}{BrightBlack}");
      }

      /// <summary>
      /// Formats ellipsis in diff.
      /// </summary>
      internal string DiffEllipsis()
      {
        if (!Enabled)
        {
          return "...";
        }

        return ApplyColor("...", $"{Dim}{BrightBlack}");
      }

      /// <summary>
      /// Formats the "Expected:" label in diff.
      /// </summary>
      internal string DiffExpectedLabel()
      {
        if (!Enabled)
        {
          return "Expected:";
        }

        return ApplyColor("  Expected ", $"{Bold}{BrightWhite}{BgGreen}") + " ";
      }

      /// <summary>
      /// Formats the "Actual:" label in diff.
      /// </summary>
      internal string DiffActualLabel()
      {
        if (!Enabled)
        {
          return "Actual:";
        }

        return ApplyColor("  Actual   ", $"{Bold}{BrightWhite}{BgRed}") + " ";
      }

      /// <summary>
      /// Formats a removed line in a diff.
      /// </summary>
      internal string DiffRemovedLine(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Dim}{BrightBlack}{BgRed}");
      }

      /// <summary>
      /// Formats an added line in a diff.
      /// </summary>
      internal string DiffAddedLine(string text)
      {
        if (!Enabled)
        {
          return text;
        }

        return ApplyColor(text, $"{Dim}{BrightBlack}{BgGreen}");
      }

      internal string DiffRemovedBackgroundCode => $"{Dim}{BrightBlack}{BgBrightRed}";
      internal string DiffAddedBackgroundCode => $"{Dim}{BrightBlack}{BgBrightGreen}";
      internal string ResetCode => Reset;

      internal string DiffRemovedInlineBackgroundCode => $"{BgRed}";
      internal string DiffAddedInlineBackgroundCode => $"{BgGreen}";

      internal string ApplyBackground(string text, string backgroundCodes)
      {
        if (!Enabled)
        {
          return text;
        }

        var rebased = text.Replace(ResetCode, $"{ResetCode}{backgroundCodes}");
        return $"{backgroundCodes}{rebased}{ResetCode}";
      }

      private static bool ShouldEnableColors()
      {
        static bool IsTruthy(string? value)
        {
          if (string.IsNullOrEmpty(value))
          {
            return false;
          }

          return !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                 && !value.Equals("0", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsFalsey(string? value)
        {
          if (string.IsNullOrEmpty(value))
          {
            return false;
          }

          return value.Equals("false", StringComparison.OrdinalIgnoreCase)
                 || value.Equals("0", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsSet(string? value) => !string.IsNullOrEmpty(value);

        var overrideColors = Environment.GetEnvironmentVariable("ASSERTIVE_COLORS_ENABLED");
        if (IsTruthy(overrideColors))
        {
          return true;
        }

        if (IsFalsey(overrideColors))
        {
          return false;
        }

        var nunitIsActive = TestFrameworkHelper.TryGetType("nunit.framework", "NUnit.Framework.AssertionException") != null;

        if (nunitIsActive)
        {
          return false;
        }

        // Visual Studio's test output doesn't render ANSI colors correctly
        if (IsSet(Environment.GetEnvironmentVariable("VisualStudioVersion")))
        {
          return false;
        }

        if (Environment.GetEnvironmentVariable("NO_COLOR") != null)
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("CI")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("TF_BUILD")))
        {
          return false; // Azure Pipelines
        }

        if (IsSet(Environment.GetEnvironmentVariable("BUILD_BUILDID")))
        {
          return false; // Azure Pipelines legacy
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("GITLAB_CI")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("CIRCLECI")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("TRAVIS")))
        {
          return false;
        }

        if (IsSet(Environment.GetEnvironmentVariable("JENKINS_HOME")))
        {
          return false;
        }

        if (IsSet(Environment.GetEnvironmentVariable("HUDSON_URL")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("TEAMCITY_VERSION")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("BUILDKITE")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("DRONE")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("APPVEYOR")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("BITBUCKET_BUILD_NUMBER")))
        {
          return false;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("BITBUCKET_PIPELINE_UUID")))
        {
          return false;
        }

        return true;
      }

      /// <summary>
      /// Formats a metadata section header (Assertion, Locals, Message, etc.)
      /// </summary>
      internal string MetadataHeader(string header)
      {
        if (!Enabled)
        {
          return $"[{header}]";
        }

        var icon = header switch
        {
          "LOCALS" => "ℹ",
          "EXCEPTION" => "⚠",
          _ => " "
        };

        return ApplyColor($" {icon} {header}".PadRight(80, ' '), $"{Bold}{Black}{BgBrightCyan}");
      }

      /// <summary>
      /// Formats expression code with C# syntax highlighting.
      /// </summary>
      internal string Expression(string expression)
      {
        if (!Enabled || !UseSyntaxHighlighting)
        {
          return expression;
        }

        return HighlightCSharpSyntax(expression);
      }

      /// <summary>
      /// Applies syntax highlighting to C# code.
      /// </summary>
      private string HighlightCSharpSyntax(string code)
      {
        var result = new System.Text.StringBuilder();
        var i = 0;

        while (i < code.Length)
        {
          var ch = code[i];

          // String literals
          if (ch is '"' or '\'')
          {
            var quote = ch;
            var str = new System.Text.StringBuilder();
            str.Append(ch);
            i++;

            var isVerbatim = i > 1 && code[i - 2] == '@';

            while (i < code.Length)
            {
              var c = code[i];
              str.Append(c);
              i++;

              if (c == quote)
              {
                // Check for escaped quote in verbatim strings
                if (isVerbatim && i < code.Length && code[i] == quote)
                {
                  str.Append(code[i]);
                  i++;
                  continue;
                }

                break;
              }

              // Handle escape sequences in regular strings
              if (!isVerbatim && c == '\\' && i < code.Length)
              {
                str.Append(code[i]);
                i++;
              }
            }

            result.Append(ApplyColor(str.ToString(), BrightGreen));
            continue;
          }

          // Verbatim string prefix
          if (ch == '@' && i + 1 < code.Length && (code[i + 1] == '"' || code[i + 1] == '\''))
          {
            result.Append(ApplyColor("@", BrightGreen));
            i++;
            continue;
          }

          // Lambda arrow =>
          if (ch == '=' && i + 1 < code.Length && code[i + 1] == '>')
          {
            result.Append(ApplyColor("=>", BrightMagenta));
            i += 2;
            continue;
          }

          // Operators and punctuation
          if ("+-*/%<>=!&|^~?:".Contains(ch))
          {
            var op = new System.Text.StringBuilder();
            op.Append(ch);
            i++;

            // Multi-character operators
            if (i < code.Length)
            {
              var next = code[i];
              if ((ch == '=' && "=>".Contains(next)) ||
                  (ch == '!' && next == '=') ||
                  (ch == '<' && next == '=') ||
                  (ch == '>' && next == '=') ||
                  (ch == '&' && next == '&') ||
                  (ch == '|' && next == '|') ||
                  (ch == '+' && next == '+') ||
                  (ch == '-' && next == '-') ||
                  (ch == '?' && next == '?'))
              {
                op.Append(next);
                i++;
              }
            }

            result.Append(ApplyColor(op.ToString(), BrightCyan));
            continue;
          }

          // Parentheses, brackets, braces
          if ("()[]{}".Contains(ch))
          {
            result.Append(ApplyColor(ch.ToString(), $"{Bold}{Blue}"));
            i++;
            continue;
          }

          // Identifiers and keywords
          if (char.IsLetter(ch) || ch == '_')
          {
            var identifier = new System.Text.StringBuilder();
            while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
            {
              identifier.Append(code[i]);
              i++;
            }

            var word = identifier.ToString();
            if (_keywords.Contains(word))
            {
              result.Append(ApplyColor(word, $"{Bold}{BrightBlue}"));
            }
            else
            {
              // Method call detection: next non-whitespace char being '('
              var j = i;
              while (j < code.Length && char.IsWhiteSpace(code[j])) j++;
              var isMethodCall = j < code.Length && code[j] == '(';
              var color = isMethodCall ? $"{Bold}{BrightMagenta}" : $"{Italic}{Yellow}";
              result.Append(ApplyColor(word, color));
            }

            continue;
          }

          // Numbers
          if (char.IsDigit(ch))
          {
            var number = new System.Text.StringBuilder();
            while (i < code.Length && (char.IsDigit(code[i]) || code[i] == '.' || code[i] == '_' ||
                                       char.ToLower(code[i]) == 'f' || char.ToLower(code[i]) == 'd' ||
                                       char.ToLower(code[i]) == 'm' || char.ToLower(code[i]) == 'l' ||
                                       char.ToLower(code[i]) == 'u' || char.ToLower(code[i]) == 'x'))
            {
              number.Append(code[i]);
              i++;

              // Handle hex numbers
              if (number.Length == 2 && number.ToString() == "0x")
              {
                while (i < code.Length && "0123456789abcdefABCDEF_".Contains(code[i]))
                {
                  number.Append(code[i]);
                  i++;
                }

                break;
              }
            }

            result.Append(ApplyColor(number.ToString(), BrightBlue));
            continue;
          }

          // Semicolons and commas
          if (ch == ';' || ch == ',' || ch == '.')
          {
            result.Append(ApplyColor(ch.ToString(), BrightWhite));
            i++;
            continue;
          }

          // Default: whitespace and other characters
          result.Append(ch);
          i++;
        }

        return result.ToString();
      }

      /// <summary>
      /// Formats a local variable name.
      /// </summary>
      internal string LocalName(string name)
      {
        if (!Enabled)
        {
          return name;
        }

        return ApplyColor(name, $"{Bold}{BrightMagenta}");
      }

      /// <summary>
      /// Formats a local variable value.
      /// </summary>
      internal string LocalValue(string value)
      {
        if (!Enabled)
        {
          return value;
        }

        return ApplyColor(value, $"{BrightWhite}");
      }
    }
  }
}
