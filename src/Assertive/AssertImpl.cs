using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

      var split = fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries);

      return string.Join("_", split);
    }

    private enum SnapshotType
    {
      Actual,
      Expected
    }

    private class AssertionState
    {
      public Dictionary<string, int> ExpressionCounter = new();
      public int GetCounter(string expression) => ExpressionCounter.TryGetValue(expression, out var count) ? count : 0;
    }

    private static ConditionalWeakTable<object, AssertionState> _assertionStates = new();

    private static string GetFileName(CurrentTestInfo currentTestInfo, string expression, AssertionState assertionState, SnapshotType type)
    {
      return EscapeFileName($"{currentTestInfo.ClassName}_{currentTestInfo.Name}_{expression}_{assertionState.GetCounter(expression)}.{(type == SnapshotType.Actual ? "actual" : "expected")}.json");
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
        
        if (!state.ExpressionCounter.TryGetValue(expression, out var count))
        {
          count = 0;
        }
        
        state.ExpressionCounter[expression] = count + 1;
        return state;
      }
    }

    public static Exception? Check(object actualObject, string expression, string sourceFile)
    {
      var testFramework = ITestFramework.GetActiveTestFramework();

      if (testFramework == null) return ExceptionHelper.GetException("No test framework could be determined.");

      var currentTestInfo = testFramework.GetCurrentTestInfo();

      if (currentTestInfo == null) return ExceptionHelper.GetException("Could not detect the currently running test.");
      
      var assertionState = UpdateState(currentTestInfo, expression);

      var sourceFileInfo = new FileInfo(sourceFile);

      var sourceFileDirectory = sourceFileInfo.Directory;

      var expectedFileInfo =
        new FileInfo(Path.Combine(sourceFileDirectory!.FullName, GetFileName(currentTestInfo, expression, assertionState, SnapshotType.Expected)));

      JsonNode expectedNode = new JsonObject();
      bool expectedExists = false;

      if (expectedFileInfo.Exists)
      {
        try
        {
          using var fileStream = expectedFileInfo.OpenRead();
          expectedNode = JsonSerializer.Deserialize<JsonNode>(fileStream, _options) ?? new JsonObject();
          expectedExists = true;
        }
        catch (JsonException) { }
      }
      else
      {
        expectedExists = false;
        expectedNode = new JsonObject();
      }

      var actualNode = JsonSerializer.SerializeToNode(actualObject, _options);

      var context = new CheckSnapshotContext { HasExistingExpected = expectedExists, UpdatedExpected = false };

      Recurse(context, expectedNode, actualNode, null);

      if (context.Errors.Count > 0)
      {
        var sb = new StringBuilder();
        sb.AppendLine("Check failed:");
        sb.AppendLine();
        sb.AppendLine("Expected:");
        sb.AppendLine(expectedExists ? expectedNode.ToJsonString(_options) : "No expected value found");
        sb.AppendLine();
        sb.AppendLine("Actual:");
        sb.AppendLine(actualNode?.ToJsonString(_options));
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (var error in context.Errors)
        {
          sb.AppendLine($"- {error.Error}");
        }

        if (Configuration.CheckSettings.LaunchDiffTool != null)
        {
          var tempDirectory = Path.GetTempPath();
          var actualTempFile = Path.Combine(tempDirectory, GetFileName(currentTestInfo, expression, assertionState, SnapshotType.Actual));

          File.WriteAllText(actualTempFile, actualNode?.ToJsonString(_options) ?? string.Empty);

          if (!expectedFileInfo.Exists)
          {
            File.WriteAllText(expectedFileInfo.FullName, "");
          }

          Configuration.CheckSettings.LaunchDiffTool(actualTempFile, expectedFileInfo.FullName);
        }

        return ExceptionHelper.GetException(sb.ToString());
      }
      else if (context.UpdatedExpected)
      {
        File.WriteAllText(expectedFileInfo.FullName, expectedNode.ToJsonString(_options));
      }

      return null;
    }

    private class CheckSnapshotContext
    {
      public List<CheckError> Errors { get; } = [];
      public required bool UpdatedExpected { get; set; }
      public required bool HasExistingExpected { get; set; }
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
        context.Errors.Add(new(propertyName != null
          ? $"{propertyName} is {actual.GetValueKind()}, expected {expected.GetValueKind()}"
          : $"Expected {expected.GetValueKind()}, got {actual.GetValueKind()}"));
        return;
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

    private static void CheckValue(CheckSnapshotContext context, JsonValue jsonValue, JsonValue actual)
    {
      if (!JsonNode.DeepEquals(jsonValue, actual))
      {
        context.Errors.Add(new($"Expected: {jsonValue}, got: {actual}"));
      }
    }

    private static void CheckArray(CheckSnapshotContext context, JsonArray jsonArray, JsonArray actual)
    {
      if (jsonArray.Count != actual.Count)
      {
        context.Errors.Add(new($"Expected array length: {jsonArray.Count}, got: {actual.Count}"));
      }

      for (var i = 0; i < jsonArray.Count; i++)
      {
        Recurse(context, jsonArray[i], actual[i], null);
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

      if (Configuration.CheckSettings.ExtraneousPropertiesOption != null)
      {
        foreach (var property in actual)
        {
          if (!expected.TryGetPropertyValue(property.Key, out _))
          {
            var handleExtraneousProperties = Configuration.CheckSettings.ExtraneousPropertiesOption(property.Key, property.Value);

            if (handleExtraneousProperties == Configuration.ExtraneousPropertiesOptions.Disallow || !context.HasExistingExpected)
            {
              context.Errors.Add(new($"Unexpected property: {property.Key}"));
            }
            else switch (handleExtraneousProperties)
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

    private static readonly JsonSerializerOptions _options = new()
    {
      TypeInfoResolver = new TypeInfoResolver(),
      IncludeFields = true,
      WriteIndented = true,
      ReferenceHandler = ReferenceHandler.IgnoreCycles,
      AllowTrailingCommas = true,
      //ReadCommentHandling = JsonCommentHandling.Allow
    };

    private class TypeInfoResolver : IJsonTypeInfoResolver
    {
      private readonly IJsonTypeInfoResolver _defaultResolver;

      public TypeInfoResolver()
      {
        _defaultResolver = new DefaultJsonTypeInfoResolver();
      }

      public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
      {
        var typeInfo = _defaultResolver.GetTypeInfo(type, options);
        if (typeInfo != null)
        {
          foreach (var property in typeInfo.Properties)
          {
            var originalGetter = property.Get;

            property.Get = (obj) =>
            {
              try
              {
                var value = originalGetter!(obj);
                return Configuration.CheckSettings.ValueRenderer?.Invoke(property, obj, value) ?? value;
              }
              catch (Exception ex)
              {
                return Configuration.CheckSettings.ExceptionRenderer(property, ex);
              }
            };

            if (Configuration.CheckSettings.ShouldIgnore != null)
            {
              property.ShouldSerialize = (obj, value) => !Configuration.CheckSettings.ShouldIgnore(property, obj, value);
            }
          }
        }

        return typeInfo;
      }
    }
  }
}