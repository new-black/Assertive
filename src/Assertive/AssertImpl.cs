using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Assertive.Analyzers;
using Assertive.Config;
using Assertive.Helpers;
using Assertive.TestFrameworks;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive
{
  internal class AssertImpl
  {
    public static Exception? That(Expression<Func<bool>> assertion, object? message, Expression<Func<object>>? context)
    {
      var compiledAssertion = assertion.Compile(true);

      Exception? exceptionToThrow = null;

      try
      {
        var result = compiledAssertion();

        if (!result)
        {
          var exceptionProvider = new FailedAssertionExceptionProvider(new Assertion(assertion, message, context));

          exceptionToThrow = exceptionProvider.GetException();
        }
      }
      catch (Exception ex)
      {
        var exceptionProvider = new FailedAssertionExceptionProvider(new Assertion(assertion, message, context));

        exceptionToThrow = exceptionProvider.GetException(ex);
      }

      return exceptionToThrow;
    }

    public static async Task<Exception?> Throws(Expression<Func<Task>> expression,
      Type? expectedExceptionType = null)
    {
      var threw = false;

      var bodyExpression = GetBodyExpression(expression);

      try
      {
        var task = (Task)expression.Compile(true).DynamicInvoke()!;

        await task;
      }
      catch (Exception ex)
      {
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex))
        {
          return ExceptionHelper.GetException(
            $"Expected {ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.GetType().FullName} instead.");
        }

        threw = true;
      }

      if (!threw)
      {
        return ExceptionHelper.GetException($"Expected {ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }

      return null;
    }

    public static Exception? Throws(LambdaExpression expression,
      Type? expectedExceptionType = null)
    {
      var threw = false;

      var bodyExpression = GetBodyExpression(expression);

      try
      {
        expression.Compile(true).DynamicInvoke();
      }
      catch (TargetInvocationException ex)
      {
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex.InnerException))
        {
          return ExceptionHelper.GetException(
            $"Expected {ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.InnerException!.GetType().FullName} instead.");
        }

        threw = true;
      }

      if (!threw)
      {
        return ExceptionHelper.GetException($"Expected {ExpressionToString(bodyExpression)} to throw an exception, but it did not.");
      }

      return null;
    }

    private static Expression GetBodyExpression(LambdaExpression expression)
    {
      Expression bodyExpression;

      if (expression.Body.NodeType == ExpressionType.Convert
          && expression.Body is UnaryExpression convertExpression
          && expression.Body.Type == typeof(object))
      {
        bodyExpression = convertExpression.Operand;
      }
      else
      {
        bodyExpression = expression.Body;
      }

      return bodyExpression;
    }

    private record CheckError(string Error);

    private static string EscapeFileName(string fileName)
    {
      var invalidChars = Path.GetInvalidFileNameChars();

      var split = fileName.Replace("\"", "").Split(invalidChars, StringSplitOptions.RemoveEmptyEntries);

      return string.Join("_", split);
    }

    private enum SnapshotType
    {
      Actual,
      Expected
    }

    private class AssertionState
    {
      public readonly Dictionary<string, int> ExpressionCounter = new();
      public int GetCounter(string expression) => ExpressionCounter.GetValueOrDefault(expression, 0);
    }

    private static readonly ConditionalWeakTable<object, AssertionState> _assertionStates = new();

    private static string GetFileName(CurrentTestInfo currentTestInfo, string expression, AssertionState assertionState,
      AssertSnapshotOptions? options, SnapshotType type)
    {
      string Identifier()
      {
        if (options?.SnapshotIdentifier != null)
        {
          return options.SnapshotIdentifier;
        }

        return $"{expression}_{assertionState.GetCounter(expression)}";
      }

      return EscapeFileName(
        $"{currentTestInfo.ClassName}_{currentTestInfo.Name}_{Identifier()}.{(type == SnapshotType.Actual ? "actual" : "expected")}.json");
    }

    private static AssertionState UpdateState(CurrentTestInfo currentTestInfo, string expression)
    {
      lock (_assertionStates)
      {
        if (!_assertionStates.TryGetValue(currentTestInfo.State, out var state))
        {
          state = new AssertionState();
          _assertionStates.Add(currentTestInfo.State, state);
        }

        var count = state.ExpressionCounter.GetValueOrDefault(expression, 0);

        state.ExpressionCounter[expression] = count + 1;
        return state;
      }
    }

    public static Exception? Check(object actualObject, AssertSnapshotOptions options, string expression, string sourceFile)
    {
      var testFramework = ITestFramework.GetActiveTestFramework();

      if (testFramework == null) return ExceptionHelper.GetException("No test framework could be determined.");

      var currentTestInfo = testFramework.GetCurrentTestInfo();

      if (currentTestInfo == null) return ExceptionHelper.GetException("Could not detect the currently running test.");

      var assertionState = UpdateState(currentTestInfo, expression);

      var sourceFileInfo = new FileInfo(sourceFile);

      var sourceFileDirectory = sourceFileInfo.Directory;

      var expectedFileInfo =
        new FileInfo(Path.Combine(sourceFileDirectory!.FullName,
          GetFileName(currentTestInfo, expression, assertionState, options, SnapshotType.Expected)));

      JsonNode expectedNode = new JsonObject();
      bool expectedExists = false;

      if (expectedFileInfo.Exists)
      {
        try
        {
          using var fileStream = expectedFileInfo.OpenRead();
          expectedNode = JsonSerializer.Deserialize<JsonNode>(fileStream, options.Configuration.JsonSerializerOptions) ?? new JsonObject();
          expectedExists = true;
        }
        catch (JsonException) { }
      }
      else
      {
        expectedExists = false;
        expectedNode = new JsonObject();
      }

      var actualNode = JsonSerializer.SerializeToNode(actualObject, options.Configuration.JsonSerializerOptions);

      var context = new CheckSnapshotContext { HasExistingExpected = expectedExists, UpdatedExpected = false, Configuration = options.Configuration };

      Recurse(context, expectedNode, actualNode, null);

      if (context.Errors.Count > 0)
      {
        var sb = new StringBuilder();
        sb.AppendLine("Check failed:");
        sb.AppendLine();
        sb.AppendLine("Expected:");
        sb.AppendLine(expectedExists ? expectedNode.ToJsonString(options.Configuration.JsonSerializerOptions) : "No expected value found");
        sb.AppendLine();
        sb.AppendLine("Actual:");
        sb.AppendLine(actualNode?.ToJsonString(options.Configuration.JsonSerializerOptions));
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (var error in context.Errors)
        {
          sb.AppendLine($"- {error.Error}");
        }

        if (Configuration.Snapshots.LaunchDiffTool != null)
        {
          var tempDirectory = Path.GetTempPath();
          var actualTempFile = Path.Combine(tempDirectory, GetFileName(currentTestInfo, expression, assertionState, options, SnapshotType.Actual));

          File.WriteAllText(actualTempFile, actualNode?.ToJsonString(options.Configuration.JsonSerializerOptions) ?? string.Empty);

          if (!expectedFileInfo.Exists)
          {
            File.WriteAllText(expectedFileInfo.FullName, "");
          }

          Configuration.Snapshots.LaunchDiffTool(actualTempFile, expectedFileInfo.FullName);
        }

        return ExceptionHelper.GetException(sb.ToString());
      }
      else if (context.UpdatedExpected)
      {
        File.WriteAllText(expectedFileInfo.FullName, expectedNode.ToJsonString(options.Configuration.JsonSerializerOptions));
      }

      return null;
    }

    private class CheckSnapshotContext
    {
      public List<CheckError> Errors { get; } = [];
      public required bool UpdatedExpected { get; set; }
      public required bool HasExistingExpected { get; init; }
      public required Configuration.CompareSnapshotsConfiguration Configuration { get; init; }
      public Dictionary<(string, int), string> CountedPlaceholderValues { get; } = new();
    }

    private static void Recurse(CheckSnapshotContext context, JsonNode? expected, JsonNode? actual, string? propertyName)
    {
      if ((expected == null) != (actual == null))
      {
        context.Errors.Add(new("Expected: " + (expected == null ? "null" : "not null") + ", got: " + (actual == null ? "null" : "not null")));
        return;
      }

      if (expected == null || actual == null)
      {
        return;
      }

      if (expected.GetValueKind() != actual.GetValueKind())
      {
        if (!(expected.GetValueKind() == JsonValueKind.String &&
              actual.GetValueKind() is JsonValueKind.Number or JsonValueKind.False or JsonValueKind.True))
        {
          context.Errors.Add(new(propertyName != null
            ? $"{propertyName} is {actual.GetValueKind()}, expected {expected.GetValueKind()}"
            : $"Expected {expected.GetValueKind()}, got {actual.GetValueKind()}"));

          return;
        }
      }

      if (expected is JsonObject jsonObject)
      {
        CheckObject(context, jsonObject, (JsonObject)actual);
      }
      else if (expected is JsonArray jsonArray)
      {
        CheckArray(context, jsonArray, (JsonArray)actual);
      }
      else if (expected is JsonValue jsonValue)
      {
        CheckValue(context, jsonValue, (JsonValue)actual);
      }
    }

    private static void CheckValue(CheckSnapshotContext context, JsonValue expected, JsonValue actual)
    {
      if (!JsonNode.DeepEquals(expected, actual))
      {
        var emitComparisonError = true;

        if (expected.GetValueKind() == JsonValueKind.String && expected.GetValue<string>() is { } value)
        {
          if (value.StartsWith(context.Configuration.PlaceholderPrefix))
          {
            var counted = value.IndexOf('#', startIndex: context.Configuration.PlaceholderPrefix.Length);
            int? count = null;
            if (counted > 0 && int.TryParse(value[(counted + 1)..], out var c))
            {
              count = c;
              value = value[..counted];
            }

            var withoutPrefix = value[context.Configuration.PlaceholderPrefix.Length..];
            var validator = context.Configuration.PlaceholderValidators.GetValueOrDefault(withoutPrefix);
            var actualValue = actual.GetValueKind() == JsonValueKind.String ? actual.GetValue<string>() : actual.ToJsonString();

            if (count.HasValue)
            {
              if (!context.CountedPlaceholderValues.TryGetValue((value, count.Value), out var previouslyEncounteredActualValue))
              {
                previouslyEncounteredActualValue = actualValue;
                context.CountedPlaceholderValues[(value, count.Value)] = actualValue;
              }

              if (actualValue != previouslyEncounteredActualValue)
              {
                context.Errors.Add(new($"Expected '{expected.GetValue<string>()}' to be {previouslyEncounteredActualValue} but it was {actualValue}"));
              }
            }

            if (validator != default && !validator.Item1(actualValue))
            {
              context.Errors.Add(new($"Value '{actualValue}' is invalid: {validator.Item2}"));
              emitComparisonError = false;
            }
            else
            {
              emitComparisonError = false;
            }
          }
        }

        if (emitComparisonError)
        {
          context.Errors.Add(new($"Expected: {expected}, got: {actual}"));
        }
      }
    }

    private static void CheckArray(CheckSnapshotContext context, JsonArray expected, JsonArray actual)
    {
      if (expected.Count != actual.Count)
      {
        context.Errors.Add(new($"Expected array length: {expected.Count}, got: {actual.Count}"));
      }

      for (var i = 0; i < expected.Count; i++)
      {
        Recurse(context, expected[i], actual[i], null);
      }
    }

    private static void CheckObject(CheckSnapshotContext context, JsonObject expected, JsonObject actual)
    {
      foreach (var property in expected)
      {
        if (!actual.TryGetPropertyValue(property.Key, out var actualValue))
        {
          context.Errors.Add(new($"Missing property: {property.Key}"));
        }

        Recurse(context, property.Value, actualValue, property.Key);
      }

      foreach (var property in actual)
      {
        if (!expected.TryGetPropertyValue(property.Key, out _))
        {
          var handleExtraneousProperties = Configuration.Snapshots.ExtraneousPropertiesOption?.Invoke(property.Key, property.Value) ?? Configuration.ExtraneousPropertiesOptions.Disallow;

          if (handleExtraneousProperties == Configuration.ExtraneousPropertiesOptions.Disallow || !context.HasExistingExpected)
          {
            context.Errors.Add(new($"Unexpected property: {property.Key}"));
          }
          else
            switch (handleExtraneousProperties)
            {
              case Configuration.ExtraneousPropertiesOptions.Ignore:
                break;
              case Configuration.ExtraneousPropertiesOptions.AutomaticUpdate:
                expected[property.Key] = property.Value?.DeepClone();
                context.UpdatedExpected = true;
                break;
              default:
                throw new ArgumentOutOfRangeException();
            }
        }
      }
    }
  }
}