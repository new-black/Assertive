using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
using Assertive.Helpers;

namespace Assertive
{
  /// <summary>
  /// Builds assertion-failure exceptions for the delegate-based pipeline: the degraded
  /// (non-intercepted) path and the failure paths of generated interceptors. Reproduces the
  /// message layout and the exception.Data contract of the expression-based
  /// FailedAssertionExceptionProvider.
  /// </summary>
  internal static class AssertionFailureBuilder
  {
    internal sealed class FailureDetails
    {
      /// <summary>The assertion source text, without the "() => " prefix.</summary>
      public required string AssertionText { get; init; }

      public object? UserMessage { get; init; }
      public Func<object?>? Context { get; init; }
      public string? ContextExpression { get; init; }

      /// <summary>Exception thrown while evaluating the assertion, if any.</summary>
      public Exception? Exception { get; init; }

      /// <summary>The pattern's expected/actual strings (pre-rendered), if a pattern applied.</summary>
      public string? Expected { get; init; }
      public string? Actual { get; init; }

      public IReadOnlyList<(string Name, object? Value)>? Locals { get; init; }
    }

    public static Exception Build(FailureDetails details)
    {
      var colors = Configuration.Colors;
      var result = new List<string>();

      if (details.Expected != null)
      {
        var friendly = details.Actual != null
          ? $"{colors.ExpectedHeader()}\n{details.Expected}\n{colors.ActualHeader()}\n{details.Actual}\n"
          : $"{colors.ExpectedHeader()}\n{details.Expected}\n";

        result.Add($"\n{details.AssertionText}\n\n{friendly}");
      }
      else
      {
        result.Add($"\n{details.AssertionText}");
      }

      if (details.UserMessage != null)
      {
        var messageContent = details.UserMessage is string s ? s : Serializer.Serialize(details.UserMessage).ToString();
        result.Add($"{colors.MetadataHeader("MESSAGE")}\n{colors.Highlight(messageContent)}");
      }

      if (details.Context != null)
      {
        object? contextValue;

        try
        {
          contextValue = details.Context();
        }
        catch (Exception ex)
        {
          contextValue = $"<context evaluation threw {ex.GetType().Name}>";
        }

        var contextText = StripLambdaPrefix(details.ContextExpression) ?? "context";
        result.Add($"{colors.MetadataHeader("CONTEXT")}\n{contextText} = {Serializer.Serialize(contextValue)}");
      }

      if (details.Exception != null)
      {
        result.Add($"""
                    {colors.MetadataHeader("EXCEPTION")}
                    {colors.Actual(details.Exception.Message)}
                    {colors.MetadataHeader("STACKTRACE")}
                    {colors.Dimmed(FilterStackTrace(details.Exception.StackTrace))}
                    """);
      }

      if (details.Locals is { Count: > 0 })
      {
        var lines = details.Locals.Select(l => $"{colors.LocalName(l.Name)}: {colors.LocalValue(Serializer.Serialize(l.Value).ToString())}");
        result.Add($"{colors.MetadataHeader("LOCALS")}\n{string.Join(Environment.NewLine, lines)}");
      }

      result.Add(colors.Dimmed(new string('·', 80)));

      var output = string.Join(Environment.NewLine, result.Select(r => r.Replace("\n", Environment.NewLine))) + Environment.NewLine;

      var exception = ExceptionHelper.GetException(Configuration.ColorScheme.NormalizeLineEndings(output));

      exception.Data["Assertive.Expected"] = details.Expected != null ? new[] { details.Expected } : Array.Empty<string>();
      exception.Data["Assertive.Actual"] = details.Actual != null ? new[] { details.Actual } : Array.Empty<string>();
      exception.Data["Assertive.HandledExceptions"] = Array.Empty<string>();

      return exception;
    }

    /// <summary>EqualsPattern/NotEqualsPattern parity from captured values.</summary>
    public static Exception BuildEquality(
      string assertionText,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      // A constant right-hand side displays as written; anything else displays its value.
      var rightDisplay = rightIsConstant ? rightSource : DisplayValue(rightValue);
      var leftDisplay = DisplayValue(leftValue);

      string expected;
      string actual;

      if (negated)
      {
        expected = $"{leftSource}: should not equal {rightDisplay}.";
        actual = $"{leftSource}: {leftDisplay}";
      }
      else
      {
        var diff = "";

        if (leftValue is string leftString && rightValue is string rightString
            && leftString != rightString
            && (leftString.Length > 10 || rightString.Length > 10))
        {
          diff = StringDiffHelper.GetStringDiff(leftString, rightString);
        }

        expected = $"{leftSource}: {rightDisplay}";
        actual = $"{leftSource}: {leftDisplay}{diff}";
      }

      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = expected,
        Actual = actual,
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>ReferenceEqualsPattern parity from captured values.</summary>
    public static Exception BuildReferenceEquals(
      string assertionText,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      var expected = negated
        ? $"{leftSource} and {rightSource} should be different instances."
        : $"{leftSource} and {rightSource} should be the same instance.";

      var actual = negated
        ? null
        : $"{leftSource}: {DisplayValue(leftValue)}\n{rightSource}: {DisplayValue(rightValue)}";

      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = expected,
        Actual = actual,
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>BoolPattern parity: a bare bool member/local was false (or true when negated).</summary>
    public static Exception BuildBool(
      string assertionText,
      string source,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = $"{source}: {(negated ? "false" : "true")}",
        Actual = negated ? "true" : "false",
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>NullPattern parity: a null check (==/!= null/default, `is object`) failed.</summary>
    public static Exception BuildNull(
      string assertionText,
      string source,
      object? value,
      bool expectedNull,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = expectedNull ? $"{source} should be null." : $"{source} should not be null.",
        Actual = expectedNull ? DisplayValue(value) : "null",
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>HasValuePattern parity: a Nullable&lt;T&gt;.HasValue check failed.</summary>
    public static Exception BuildHasValue(
      string assertionText,
      string source,
      object? value,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = negated ? $"{source} should not have a value." : $"{source} should have a value.",
        Actual = negated ? $"Value: {DisplayValue(value)}." : "It was null.",
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>IsPattern parity: an `is T` type check failed.</summary>
    public static Exception BuildIsType(
      string assertionText,
      string source,
      object? value,
      Type expectedType,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = $"{source} should {(negated ? "not " : "")}be of type {TypeHelper.TypeNameToString(expectedType)}.",
        Actual = value == null ? "It was null." : $"Type: {TypeHelper.TypeNameToString(value.GetType())}.",
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>LessThanOrGreaterThanPattern parity: a numeric comparison failed.</summary>
    public static Exception BuildComparison(
      string assertionText,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      string comparisonLabel,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      var actual = rightIsConstant
        ? $"{leftSource}: {DisplayValue(leftValue)}."
        : $"{leftSource}: {DisplayValue(leftValue)}\n{rightSource}: {DisplayValue(rightValue)}";

      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = $"{leftSource} should be {comparisonLabel} {rightSource}.",
        Actual = actual,
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>LengthPattern parity: a Length/Count comparison failed.</summary>
    public static Exception BuildLength(
      string assertionText,
      string operandSource,
      string? filterSource,
      string countLabel,
      string comparisonLabel,
      object? actualLength,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      var filter = filterSource != null ? $" with filter {filterSource}" : "";

      var expected = rightIsConstant
        ? $"{operandSource}{filter} should have a {countLabel} {comparisonLabel} {rightSource}."
        : $"{operandSource}{filter} should have a {countLabel} {comparisonLabel} {rightSource} (value: {DisplayValue(rightValue)}).";

      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = expected,
        Actual = $"{countLabel}: {DisplayValue(actualLength)}.",
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>
    /// Serialized value display, with enum values qualified by their type name (the
    /// expression-based pipeline did this through ExpressionEnumValue).
    /// </summary>
    private static string DisplayValue(object? value)
    {
      if (value != null)
      {
        var type = value.GetType();

        if (type.IsEnum && Enum.IsDefined(type, value))
        {
          return $"{type.Name}.{value}";
        }
      }

      return Serializer.Serialize(value).ToString();
    }

    internal static string? StripLambdaPrefix(string? expression)
    {
      const string lambdaPrefix = "() => ";

      if (expression != null && expression.StartsWith(lambdaPrefix, StringComparison.Ordinal))
      {
        return expression.Substring(lambdaPrefix.Length);
      }

      return expression;
    }

    private static string FilterStackTrace(string? stackTrace)
    {
      if (string.IsNullOrEmpty(stackTrace))
      {
        return "";
      }

      var lines = stackTrace!.Split(Environment.NewLine, StringSplitOptions.None);
      var filteredLines = lines.Where(line =>
        !line.Contains("Assertive.AssertImpl.") &&
        !line.Contains("Assertive.Assert.") &&
        !line.Contains("Assertive.Generated.") &&
        !line.Contains("Assertive.AssertionFailureBuilder") &&
        !line.Contains("Assertive.Runtime.GeneratedAssert"));

      return string.Join(Environment.NewLine, filteredLines);
    }
  }
}
