using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Assertive.Config;
using Assertive.Helpers;
using Assertive.TestFrameworks;

namespace Assertive;

internal partial class AssertImpl
{
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

    string TestName()
    {
      if (currentTestInfo.Arguments == null || currentTestInfo.Arguments.Length == 0)
      {
        return currentTestInfo.Name;
      }

      var arguments = currentTestInfo.Arguments.Select(a => a?.ToString() ?? "null").ToArray();

      return $"{currentTestInfo.Name}({string.Join(", ", arguments)})";
    }

    return EscapeFileName(
      $"{currentTestInfo.ClassName}.{TestName()}#{Identifier()}.{(type == SnapshotType.Actual ? "actual" : "expected")}.json");
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

  public static Exception? Snapshot(object actualObject, AssertSnapshotOptions options, string expression, string sourceFile)
  {
    var testFramework = ITestFramework.GetActiveTestFramework();

    if (testFramework == null) return ExceptionHelper.GetException("No test framework could be determined.");

    var currentTestInfo = testFramework.GetCurrentTestInfo();

    if (currentTestInfo == null) return ExceptionHelper.GetException("Could not detect the currently running test.");

    var assertionState = UpdateState(currentTestInfo, expression);

    var sourceFileInfo = new FileInfo(sourceFile);

    var sourceFileDirectory = sourceFileInfo.Directory;

    string GetExpectedFileDirectory()
    {
      if (options.Configuration.ExpectedFileDirectoryResolver == null)
      {
        return sourceFileDirectory!.FullName;
      }

      return options.Configuration.ExpectedFileDirectoryResolver(currentTestInfo.Method, sourceFileInfo);
    }

    var serializerOptions = options.Configuration.GetJsonSerializerOptions();

    var expectedFileInfo =
      new FileInfo(Path.Combine(GetExpectedFileDirectory(),
        GetFileName(currentTestInfo, expression, assertionState, options, SnapshotType.Expected)));

    JsonNode expectedNode = new JsonObject();
    bool expectedFileExists = false;

    if (expectedFileInfo.Exists)
    {
      try
      {
        using var fileStream = expectedFileInfo.OpenRead();
        expectedNode = JsonSerializer.Deserialize<JsonNode>(fileStream, serializerOptions) ?? new JsonObject();
        expectedFileExists = true;
      }
      catch (JsonException) { }
    }
    else
    {
      expectedFileExists = false;
      expectedNode = new JsonObject();
    }

    var actualNode = JsonSerializer.SerializeToNode(actualObject, serializerOptions);

    var context = new CheckSnapshotContext { ExpectedFileExists = expectedFileExists, UpdatedExpected = false, Configuration = options.Configuration };

    CheckRecursive(context, expectedNode, actualNode, null);

    if (options.Configuration.TreatAllSnapshotsAsCorrect && !expectedFileInfo.Exists)
    {
      EnsureExpectedDirectory(expectedFileInfo);

      File.WriteAllText(expectedFileInfo.FullName, SerializeActual(serializerOptions, actualNode));
      return null;
    }

    if (context.Errors.Count > 0)
    {
      var sb = new StringBuilder();
      sb.AppendLine("Check failed:");
      sb.AppendLine();
      sb.AppendLine("Expected:");
      sb.AppendLine(expectedFileExists ? expectedNode.ToJsonString(serializerOptions) : "No expected value found");
      sb.AppendLine();
      sb.AppendLine("Actual:");
      sb.AppendLine(actualNode?.ToJsonString(serializerOptions));
      sb.AppendLine();
      sb.AppendLine("Errors:");

      foreach (var error in context.Errors)
      {
        sb.AppendLine($"- {error.Error}");
      }

      if (options.Configuration.LaunchDiffTool != null)
      {
        var tempDirectory = Path.GetTempPath();
        var actualTempFile = Path.Combine(tempDirectory, GetFileName(currentTestInfo, expression, assertionState, options, SnapshotType.Actual));

        File.WriteAllText(actualTempFile, SerializeActual(serializerOptions, actualNode));

        if (!expectedFileInfo.Exists)
        {
          EnsureExpectedDirectory(expectedFileInfo);

          File.WriteAllText(expectedFileInfo.FullName, "");
        }

        options.Configuration.LaunchDiffTool(actualTempFile, expectedFileInfo.FullName);
      }
      else if (!expectedFileInfo.Exists)
      {
        EnsureExpectedDirectory(expectedFileInfo);

        File.WriteAllText(expectedFileInfo.FullName, "");
      }

      return ExceptionHelper.GetException(sb.ToString());
    }
    else if (context.UpdatedExpected)
    {
      File.WriteAllText(expectedFileInfo.FullName, expectedNode.ToJsonString(serializerOptions));
    }

    return null;
  }

  private static string SerializeActual(JsonSerializerOptions options, JsonNode? actualNode)
  {
    return actualNode?.ToJsonString(options) ?? string.Empty;
  }

  private static void EnsureExpectedDirectory(FileInfo expectedFileInfo)
  {
    if (expectedFileInfo.DirectoryName != null && !Directory.Exists(expectedFileInfo.DirectoryName))
    {
      Directory.CreateDirectory(expectedFileInfo.DirectoryName);
    }
  }

  private class CheckSnapshotContext
  {
    public List<CheckError> Errors { get; } = [];
    public required bool UpdatedExpected { get; set; }
    public required bool ExpectedFileExists { get; init; }
    public required Configuration.CompareSnapshotsConfiguration Configuration { get; init; }
    public Dictionary<(string, int), string> CountedPlaceholderValues { get; } = new();
  }

  private static void CheckRecursive(CheckSnapshotContext context, JsonNode? expected, JsonNode? actual, string? propertyName)
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
        if (value.StartsWith(context.Configuration.Normalization.PlaceholderPrefix))
        {
          var counted = value.IndexOf('#', startIndex: context.Configuration.Normalization.PlaceholderPrefix.Length);
          int? count = null;
          if (counted > 0 && int.TryParse(value[(counted + 1)..], out var c))
          {
            count = c;
            value = value[..counted];
          }

          var withoutPrefix = value[context.Configuration.Normalization.PlaceholderPrefix.Length..];
          var validator = context.Configuration.Normalization.PlaceholderValidatorsLookup.GetValueOrDefault(withoutPrefix);
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

          bool ExecuteValidator()
          {
            try
            {
              return validator.Item1(actualValue);
            }
            catch (Exception e)
            {
              context.Errors.Add(new($"Error while executing placeholder validator for '{withoutPrefix}': {e.Message}"));
              return false;
            }
          }

          if (validator != default && !ExecuteValidator())
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
      CheckRecursive(context, expected[i], actual[i], null);
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

      CheckRecursive(context, property.Value, actualValue, property.Key);
    }

    foreach (var property in actual)
    {
      if (!expected.TryGetPropertyValue(property.Key, out _))
      {
        var handleExtraneousProperties = context.Configuration.ExtraneousProperties?.Invoke(property.Key, property.Value) ??
                                         Configuration.ExtraneousPropertiesOptions.Disallow;

        if (handleExtraneousProperties == Configuration.ExtraneousPropertiesOptions.Disallow || !context.ExpectedFileExists)
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

  private record CheckError(string Error);
}