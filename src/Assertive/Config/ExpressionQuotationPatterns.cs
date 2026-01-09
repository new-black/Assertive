namespace Assertive.Config
{
  /// <summary>
  /// Predefined patterns for quoting assertion expressions in test output.
  /// Use with <see cref="Configuration.ExpressionQuotationPattern"/>.
  /// </summary>
  /// <example>
  /// <code>
  /// Configuration.ExpressionQuotationPattern = ExpressionQuotationPatterns.Backticks;
  /// </code>
  /// </example>
  public static class ExpressionQuotationPatterns
  {
    /// <summary>No quotation, expression is rendered as-is.</summary>
    public const string None = "{0}";

    /// <summary>Wraps expression in backticks: `expression`</summary>
    public const string Backticks = "`{0}`";

    /// <summary>Wraps expression in curly brackets: {expression}</summary>
    public const string CurlyBrackets = "{{{0}}}";

    /// <summary>Wraps expression in square brackets: [expression]</summary>
    public const string SquareBrackets = "[{0}]";

    /// <summary>Wraps expression in round brackets (parentheses): (expression)</summary>
    public const string RoundBrackets = "({0})";

    /// <summary>Wraps expression in angle brackets: &lt;expression&gt;</summary>
    public const string AngleBrackets = "<{0}>";

    /// <summary>Wraps expression in double quotes: "expression"</summary>
    public const string DoubleQuotes = "\"{0}\"";

    /// <summary>Wraps expression in single quotes: 'expression'</summary>
    public const string SingleQuotes = "'{0}'";
  }
}
