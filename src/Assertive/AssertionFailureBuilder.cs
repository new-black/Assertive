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

      leftSource = Q(leftSource);

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
      leftSource = Q(leftSource);
      rightSource = Q(rightSource);

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
        Expected = $"{Q(source)}: {(negated ? "false" : "true")}",
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
        Expected = expectedNull ? $"{Q(source)} should be null." : $"{Q(source)} should not be null.",
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
        Expected = negated ? $"{Q(source)} should not have a value." : $"{Q(source)} should have a value.",
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
        Expected = $"{Q(source)} should {(negated ? "not " : "")}be of type {TypeHelper.TypeNameToString(expectedType)}.",
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
      var rightSourceDisplay = rightIsConstant ? rightSource : Q(rightSource);

      leftSource = Q(leftSource);

      var actual = rightIsConstant
        ? $"{leftSource}: {DisplayValue(leftValue)}."
        : $"{leftSource}: {DisplayValue(leftValue)}\n{rightSourceDisplay}: {DisplayValue(rightValue)}";

      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = $"{leftSource} should be {comparisonLabel} {rightSourceDisplay}.",
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
      var filter = filterSource != null ? $" with filter {Q(filterSource)}" : "";
      var rightSourceDisplay = rightIsConstant ? rightSource : Q(rightSource);

      operandSource = Q(operandSource);

      var expected = rightIsConstant
        ? $"{operandSource}{filter} should have a {countLabel} {comparisonLabel} {rightSourceDisplay}."
        : $"{operandSource}{filter} should have a {countLabel} {comparisonLabel} {rightSourceDisplay} (value: {DisplayValue(rightValue)}).";

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

    /// <summary>ContainsPattern parity: a string/collection Contains call returned false.</summary>
    public static Exception BuildContains(
      string assertionText,
      string instanceSource,
      object? instanceValue,
      string expectedSource,
      object? expectedValue,
      bool expectedIsConstant,
      bool stringInstance,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      instanceSource = Q(instanceSource);

      var expectedValueString = expectedIsConstant
        ? expectedSource
        : $"{Q(expectedSource)} (value: {DisplayValue(expectedValue)})";

      string expected;
      string actual;

      if (stringInstance)
      {
        var hint = !negated ? GetStringContainsHint(instanceValue as string, expectedValue as string) : "";

        expected = $"{instanceSource} should{(negated ? " not " : " ")}contain the substring {expectedValueString}.";
        actual = $"{instanceSource}: {DisplayValue(instanceValue)}{hint}";
      }
      else
      {
        expected = $"{instanceSource} should{(negated ? " not " : " ")}contain {expectedValueString}.";
        actual = $"{instanceSource}: {DisplayValue(instanceValue)}";
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

    /// <summary>Mirrors ContainsPattern.GetStringContainsHint: line-ending/casing/closest-match hints.</summary>
    private static string GetStringContainsHint(string? actualValue, string? expectedValue)
    {
      try
      {
        if (actualValue == null || expectedValue == null)
        {
          return "";
        }

        var colors = Configuration.Colors;
        var hints = new List<string>();

        var normalizedActual = actualValue.Replace("\r\n", "\n").Replace("\r", "\n");
        var normalizedExpected = expectedValue.Replace("\r\n", "\n").Replace("\r", "\n");

        if (normalizedActual.Contains(normalizedExpected))
        {
          hints.Add(colors.Dimmed("The strings differ only in line endings"));
          hints.Add(StringDiffHelper.GetStringDiff(expectedValue, actualValue));
        }
        else if (actualValue.Contains(expectedValue, StringComparison.OrdinalIgnoreCase))
        {
          hints.Add(colors.Dimmed("The strings differ only in casing"));
        }
        else if (StringDiffHelper.GetClosestSubstringDiff(actualValue, expectedValue) is { } closestMatch)
        {
          hints.Add(closestMatch);
        }

        if (hints.Count > 0)
        {
          return "\n" + string.Join("\n", hints);
        }
      }
      catch
      {
        // Don't let hint generation break the assertion message.
      }

      return "";
    }

    /// <summary>StartsWithAndEndsWithPattern parity.</summary>
    public static Exception BuildStartsEndsWith(
      string assertionText,
      string instanceSource,
      object? instanceValue,
      string argSource,
      object? argValue,
      bool argIsConstant,
      string methodLabel,
      bool negated,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      instanceSource = Q(instanceSource);

      var argSourceDisplay = argIsConstant ? argSource : Q(argSource);

      var expected = argIsConstant
        ? $"{instanceSource}: should{(negated ? " not " : " ")}{methodLabel} {argSourceDisplay}."
        : $"{instanceSource}: should{(negated ? " not " : " ")}{methodLabel} {argSourceDisplay}.\n\n{argSourceDisplay}: {DisplayValue(argValue)}";

      return Build(new FailureDetails
      {
        AssertionText = assertionText,
        Expected = expected,
        Actual = $"{instanceSource}: {DisplayValue(instanceValue)}",
        Locals = locals,
        UserMessage = userMessage,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>
    /// AnyPattern parity. For negated assertions, count is the number of (filter-matching)
    /// items; otherwise it is the unfiltered item count (used to phrase the actual message).
    /// </summary>
    public static Exception BuildAny(
      string assertionText,
      string collectionSource,
      string? filterSource,
      bool negated,
      int count,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      collectionSource = Q(collectionSource);

      var filterString = filterSource != null ? $" that match the filter {Q(filterSource)}" : "";

      string expected;
      string actual;

      if (negated)
      {
        expected = $"Collection {collectionSource} should not contain any items{filterString}.";
        actual = $"It contained {count} {(count == 1 ? "item" : "items")}";
      }
      else
      {
        expected = $"Collection {collectionSource} should contain some items{filterString}.";
        actual = filterSource == null || count == 0 ? "It contained no items." : "It contained no items matching the filter.";
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

    /// <summary>SequenceEqualPattern parity: element-wise diff of the two sequences.</summary>
    public static Exception BuildSequenceEqual(
      string assertionText,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      object? comparer,
      Type? elementType,
      IReadOnlyList<(string Name, object? Value)>? locals,
      object? userMessage,
      Func<object?>? context,
      string? contextExpression)
    {
      leftSource = Q(leftSource);
      rightSource = Q(rightSource);

      var sequence1 = ((System.Collections.IEnumerable?)leftValue)?.Cast<object?>() ?? Enumerable.Empty<object?>();
      var sequence2 = ((System.Collections.IEnumerable?)rightValue)?.Cast<object?>() ?? Enumerable.Empty<object?>();

      var equals = GetSequenceComparer(comparer, elementType);

      var expected = $"{leftSource} should equal {rightSource}";

      string actual;

      if (equals != null)
      {
        var differences = new List<string>();
        var differenceCount = 0;
        var hasMoreDifferences = false;
        var index = 0;

        using (var enumerator1 = sequence1.GetEnumerator())
        using (var enumerator2 = sequence2.GetEnumerator())
        {
          while (true)
          {
            var moveNext1 = enumerator1.MoveNext();
            var moveNext2 = enumerator2.MoveNext();

            if (!moveNext1 && !moveNext2)
            {
              break;
            }

            var current1 = moveNext1 ? enumerator1.Current : null;
            var current2 = moveNext2 ? enumerator2.Current : null;

            if (!equals(current1, current2))
            {
              differenceCount++;

              if (differences.Count == 10)
              {
                hasMoreDifferences = true;
              }
              else
              {
                differences.Add(
                  $"[{index}]: {(moveNext1 ? Serializer.Serialize(current1).ToString() : "(no value)")} {Configuration.Colors.Expression("<>")} {(moveNext2 ? Serializer.Serialize(current2).ToString() : "(no value)")}");
              }
            }

            index++;
          }
        }

        actual = $"""
                  There {(differenceCount > 1 ? $"were {differenceCount} differences" : "was 1 difference")}{(hasMoreDifferences ? " (first 10)" : "")}:

                  {string.Join("," + Environment.NewLine, differences)}

                  {leftSource}: {Serializer.Serialize(sequence1)}
                  {rightSource}: {Serializer.Serialize(sequence2)}
                  """;
      }
      else
      {
        actual = $"""
                  {leftSource}: {Serializer.Serialize(sequence1)}
                  {rightSource}: {Serializer.Serialize(sequence2)}
                  """;
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

    private static Func<object?, object?, bool>? GetSequenceComparer(object? comparer, Type? elementType)
    {
      if (comparer == null && elementType != null)
      {
        comparer = typeof(EqualityComparer<>).MakeGenericType(elementType)
          .GetProperty(nameof(EqualityComparer<int>.Default))?.GetValue(null);
      }

      switch (comparer)
      {
        case null:
          return null;

        case System.Collections.IEqualityComparer nonGeneric:
          return (x, y) => nonGeneric.Equals(x, y);

        default:
        {
          if (elementType == null)
          {
            return null;
          }

          var comparerInterface = comparer.GetType().GetInterfaces().FirstOrDefault(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IEqualityComparer<>)
            && i.GenericTypeArguments.Length == 1
            && i.GenericTypeArguments[0].IsAssignableFrom(elementType));

          var equalsMethod = comparerInterface?.GetMethod(nameof(IEqualityComparer<int>.Equals));

          if (equalsMethod == null)
          {
            return null;
          }

          var capturedComparer = comparer;
          return (x, y) => equalsMethod.Invoke(capturedComparer, new[] { x, y }) is true;
        }
      }
    }

    /// <summary>
    /// Applies the configured expression quotation pattern to an expression's source text
    /// (the expression-based pipeline applied this in ExpressionToString). Constants are
    /// never quoted; callers skip this for constant sources.
    /// </summary>
    private static string Q(string source)
    {
      return Configuration.ExpressionQuotationPattern is { } pattern
        ? string.Format(pattern, source)
        : source;
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
