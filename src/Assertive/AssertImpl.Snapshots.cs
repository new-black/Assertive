using System;
using System.Collections.Generic;
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
    AssertSnapshotOptions? options, SnapshotType type, bool isStringSnapshot = false)
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

    var extension = isStringSnapshot ? "txt" : "json";
    return EscapeFileName(
      $"{currentTestInfo.ClassName}.{TestName()}#{Identifier()}.{(type == SnapshotType.Actual ? "actual" : "expected")}.{extension}");
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

    if (testFramework == null)
    {
      return ExceptionHelper.GetException("No test framework could be determined.");
    }

    var currentTestInfo = testFramework.GetCurrentTestInfo();

    if (currentTestInfo == null)
    {
      return ExceptionHelper.GetException("Could not detect the currently running test.");
    }

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

    // Determine if this is a string snapshot to use the correct file extension
    var isStringSnapshot = actualObject is string;

    var expectedFileInfo =
      new FileInfo(Path.Combine(GetExpectedFileDirectory(),
        GetFileName(currentTestInfo, expression, assertionState, options, SnapshotType.Expected, isStringSnapshot)));

    // Handle string snapshots differently - store as plain text without JSON serialization
    if (isStringSnapshot)
    {
      var actualString = (string)actualObject;
      var expectedString = expectedFileInfo.Exists ? File.ReadAllText(expectedFileInfo.FullName) : "";

      if (TryAcceptSnapshot(expectedFileInfo, options, actualString))
      {
        return null;
      }

      if (actualString == expectedString)
      {
        return null;
      }

      return BuildStringSnapshotError(actualString, expectedString, expectedFileInfo, options, currentTestInfo, expression, assertionState);
    }

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

    var actualNode = SerializeToNode(actualObject, serializerOptions);
    var actualJson = SerializeActual(serializerOptions, actualNode);

    if (TryAcceptSnapshot(expectedFileInfo, options, actualJson))
    {
      return null;
    }

    var context = new CheckSnapshotContext { ExpectedFileExists = expectedFileExists, UpdatedExpected = false, Configuration = options.Configuration };

    CheckRecursive(context, expectedNode, actualNode, "$");

    if (context.Errors.Count > 0)
    {
      var colors = Config.Configuration.Colors;
      var sb = new StringBuilder();

      if (!expectedFileExists)
      {
        sb.AppendLine();
        sb.AppendLine(colors.Dimmed("No expected snapshot exists yet. Copy the actual value to the expected file to accept."));
      }

      sb.AppendLine();
      sb.AppendLine(colors.ExpectedHeader());

      var expectedJson = expectedNode.ToJsonString(serializerOptions);

      sb.AppendLine(expectedFileExists ? expectedJson.Length < 100 ? colors.Expression(expectedJson) : expectedJson : colors.Dimmed("(new snapshot)"));

      sb.AppendLine();
      sb.AppendLine(colors.ActualHeader());
      sb.AppendLine(actualJson.Length < 100 ? colors.Expression(actualJson) : actualJson);

      sb.AppendLine();
      sb.AppendLine(colors.MetadataHeader("ERRORS"));

      foreach (var error in context.Errors)
      {
        sb.AppendLine();
        sb.AppendLine($"  {colors.Highlight(error.Path)}");

        var description = error.Type switch
        {
          CheckErrorType.ValueMismatch => "Value mismatch",
          CheckErrorType.TypeMismatch => "Type mismatch",
          CheckErrorType.MissingProperty => "Missing property",
          CheckErrorType.UnexpectedProperty => "Unexpected property",
          CheckErrorType.ArrayLengthMismatch => "Array length mismatch",
          CheckErrorType.NullMismatch => "Null mismatch",
          CheckErrorType.PlaceholderValidation => error.Message ?? "Placeholder validation failed",
          CheckErrorType.PlaceholderCountMismatch => error.Message ?? "Placeholder count mismatch",
          _ => "Error"
        };

        sb.AppendLine($"  {colors.DiffHeader(description)}");

        if (error.Expected != null)
        {
          sb.AppendLine($"    Expected: {colors.Expected(error.Expected)}");
        }

        if (error.Actual != null)
        {
          sb.AppendLine($"    Actual:   {colors.Actual(error.Actual)}");
        }
      }

      sb.AppendLine();
      sb.AppendLine(colors.MetadataHeader("SNAPSHOT FILE"));
      sb.AppendLine(colors.Highlight(expectedFileInfo.FullName));

      sb.AppendLine(colors.Dimmed(new string('·', 80)));

      LaunchDiffToolIfConfigured(options, expectedFileInfo, actualJson, currentTestInfo, expression, assertionState, isStringSnapshot: false);

      return ExceptionHelper.GetException(sb.ToString());
    }
    else if (context.UpdatedExpected)
    {
      File.WriteAllText(expectedFileInfo.FullName, expectedNode.ToJsonString(serializerOptions));
    }

    return null;
  }

  private static bool TryAcceptSnapshot(FileInfo expectedFileInfo, AssertSnapshotOptions options, string content)
  {
    // TreatAllSnapshotsAsCorrect: accept any snapshot (new or existing), overwriting expected files
    if (options.Configuration.TreatAllSnapshotsAsCorrect)
    {
      EnsureExpectedDirectory(expectedFileInfo);
      File.WriteAllText(expectedFileInfo.FullName, content);
      return true;
    }

    // AcceptNewSnapshots: only accept new snapshots where no expected file exists
    if (options.Configuration.AcceptNewSnapshots && !expectedFileInfo.Exists)
    {
      EnsureExpectedDirectory(expectedFileInfo);
      File.WriteAllText(expectedFileInfo.FullName, content);
      return true;
    }

    return false;
  }

  private static bool ShouldLaunchDiffTool()
  {
    // Don't launch diff tool if explicitly disabled
    if (Environment.GetEnvironmentVariable("ASSERTIVE_DISABLE_DIFF_TOOL") != null)
    {
      return false;
    }

    // Don't launch diff tool when running in LLM/AI coding assistant contexts
    if (Environment.GetEnvironmentVariable("CLAUDECODE") != null) // Claude Code
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("CURSOR_IDE") != null) // Cursor
    {
      return false;
    }
    
    if (Environment.GetEnvironmentVariable("AIDER") != null) // Aider AI
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("CODEX_THREAD_ID") != null) // OpenAI Codex
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("CONTINUE_GLOBAL_DIR") != null) // Continue.dev
    {
      return false;
    }

    // Don't launch diff tool in CI/automated environments
    if (Environment.GetEnvironmentVariable("CI") != null)
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != null)
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("TF_BUILD") != null) // Azure Pipelines
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("JENKINS_URL") != null)
    {
      return false;
    }

    if (Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null)
    {
      return false;
    }

    // Check if running in a non-interactive terminal
    try
    {
      if (!Environment.UserInteractive)
      {
        return false;
      }
    }
    catch
    {
      // If we can't determine, assume non-interactive
      return false;
    }

    return true;
  }

  private static void LaunchDiffToolIfConfigured(AssertSnapshotOptions options, FileInfo expectedFileInfo,
    string actualContent, CurrentTestInfo currentTestInfo, string expression, AssertionState assertionState, bool isStringSnapshot)
  {
    if (options.Configuration.LaunchDiffTool != null && ShouldLaunchDiffTool())
    {
      var tempDirectory = Path.GetTempPath();
      var actualTempFile = Path.Combine(tempDirectory, GetFileName(currentTestInfo, expression, assertionState, options, SnapshotType.Actual, isStringSnapshot));

      File.WriteAllText(actualTempFile, actualContent);

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
  }

  private static Exception BuildStringSnapshotError(string actualString, string expectedString, FileInfo expectedFileInfo,
    AssertSnapshotOptions options, CurrentTestInfo currentTestInfo, string expression, AssertionState assertionState)
  {
    var colors = Config.Configuration.Colors;
    var sb = new StringBuilder();
    var expectedFileExists = expectedFileInfo.Exists;

    if (!expectedFileExists)
    {
      sb.AppendLine();
      sb.AppendLine(colors.Dimmed("No expected snapshot exists yet. Copy the actual value to the expected file to accept."));
    }

    // Use string diff for detailed comparison
    sb.Append(StringDiffHelper.GetStringDiff(actualString, expectedString));

    sb.AppendLine();
    sb.AppendLine(colors.MetadataHeader("SNAPSHOT FILE"));
    sb.AppendLine(colors.Highlight(expectedFileInfo.FullName));

    sb.AppendLine(colors.Dimmed(new string('·', 80)));

    LaunchDiffToolIfConfigured(options, expectedFileInfo, actualString, currentTestInfo, expression, assertionState, isStringSnapshot: true);

    return ExceptionHelper.GetException(sb.ToString());
  }

  private static JsonNode? SerializeToNode(object actualObject, JsonSerializerOptions serializerOptions)
  {
    // System.Text.Json JsonNode types - use directly (DeepClone to detach from parent)
    if (actualObject is JsonNode jsonNode)
    {
      return jsonNode.DeepClone();
    }

    // System.Text.Json JsonDocument - extract the root element
    if (actualObject is JsonDocument jsonDocument)
    {
      return JsonNode.Parse(jsonDocument.RootElement.GetRawText());
    }

    // System.Text.Json JsonElement - parse from raw text
    if (actualObject is JsonElement jsonElement)
    {
      return JsonNode.Parse(jsonElement.GetRawText());
    }

    // Newtonsoft.Json types - convert via their JSON string representation
    // Check by namespace to avoid a direct dependency on Newtonsoft.Json
    if (actualObject.GetType().Namespace == "Newtonsoft.Json.Linq")
    {
      var json = actualObject.ToString()!;
      return JsonNode.Parse(json);
    }

    return JsonSerializer.SerializeToNode(actualObject, serializerOptions);
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

  private enum CheckErrorType
  {
    ValueMismatch,
    TypeMismatch,
    MissingProperty,
    UnexpectedProperty,
    ArrayLengthMismatch,
    NullMismatch,
    PlaceholderValidation,
    PlaceholderCountMismatch
  }

  private record CheckError(
    CheckErrorType Type,
    string Path,
    string? Expected = null,
    string? Actual = null,
    string? Message = null
  );

  private static string BuildPath(string? basePath, string segment)
  {
    if (string.IsNullOrEmpty(basePath))
    {
      return segment;
    }

    if (segment.StartsWith("["))
    {
      return basePath + segment;
    }

    return basePath + "." + segment;
  }

  private static void CheckRecursive(CheckSnapshotContext context, JsonNode? expected, JsonNode? actual, string path)
  {
    if ((expected == null) != (actual == null))
    {
      context.Errors.Add(new(CheckErrorType.NullMismatch, path,
        Expected: expected == null ? "null" : "not null",
        Actual: actual == null ? "null" : "not null"));
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
        context.Errors.Add(new(CheckErrorType.TypeMismatch, path,
          Expected: expected.GetValueKind().ToString(),
          Actual: actual.GetValueKind().ToString()));
        return;
      }
    }

    if (expected is JsonObject jsonObject)
    {
      CheckObject(context, jsonObject, (JsonObject)actual, path);
    }
    else if (expected is JsonArray jsonArray)
    {
      CheckArray(context, jsonArray, (JsonArray)actual, path);
    }
    else if (expected is JsonValue jsonValue)
    {
      CheckValue(context, jsonValue, (JsonValue)actual, path);
    }
  }

  private static void CheckValue(CheckSnapshotContext context, JsonValue expected, JsonValue actual, string path)
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
              context.Errors.Add(new(CheckErrorType.PlaceholderCountMismatch, path,
                Expected: previouslyEncounteredActualValue,
                Actual: actualValue,
                Message: $"Expected '{expected.GetValue<string>()}' to be {previouslyEncounteredActualValue} but was {actualValue}"));
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
              context.Errors.Add(new(CheckErrorType.PlaceholderValidation, path,
                Actual: actualValue,
                Message: $"Error while executing placeholder validator for '{withoutPrefix}': {e.Message}"));
              return false;
            }
          }

          if (validator != default && !ExecuteValidator())
          {
            context.Errors.Add(new(CheckErrorType.PlaceholderValidation, path,
              Actual: actualValue,
              Message: validator.Item2));
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
        context.Errors.Add(new(CheckErrorType.ValueMismatch, path,
          Expected: expected.ToJsonString(),
          Actual: actual.ToJsonString()));
      }
    }
  }

  private static void CheckArray(CheckSnapshotContext context, JsonArray expected, JsonArray actual, string path)
  {
    if (expected.Count != actual.Count)
    {
      context.Errors.Add(new(CheckErrorType.ArrayLengthMismatch, path,
        Expected: expected.Count.ToString(),
        Actual: actual.Count.ToString()));
    }

    for (var i = 0; i < expected.Count && i < actual.Count; i++)
    {
      CheckRecursive(context, expected[i], actual[i], BuildPath(path, $"[{i}]"));
    }
  }

  private static void CheckObject(CheckSnapshotContext context, JsonObject expected, JsonObject actual, string path)
  {
    foreach (var property in expected)
    {
      var propertyPath = BuildPath(path, property.Key);

      if (!actual.TryGetPropertyValue(property.Key, out var actualValue))
      {
        context.Errors.Add(new(CheckErrorType.MissingProperty, propertyPath));
      }

      CheckRecursive(context, property.Value, actualValue, propertyPath);
    }

    foreach (var property in actual)
    {
      if (!expected.TryGetPropertyValue(property.Key, out _))
      {
        var propertyPath = BuildPath(path, property.Key);
        var handleExtraneousProperties = context.Configuration.ExtraneousProperties?.Invoke(property.Key, property.Value) ??
                                         Configuration.ExtraneousPropertiesOptions.Disallow;

        if (handleExtraneousProperties == Configuration.ExtraneousPropertiesOptions.Disallow || !context.ExpectedFileExists)
        {
          context.Errors.Add(new(CheckErrorType.UnexpectedProperty, propertyPath,
            Actual: property.Value?.ToJsonString()));
        }
        else
        {
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