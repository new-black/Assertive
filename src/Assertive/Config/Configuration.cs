using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Assertive.Config
{
  public static class Configuration
  {
    /// <summary>
    /// Determines if and how assertion expressions in the test output should be quoted.
    /// This can make the expression easier to distinguish from the surrounding text.
    /// By default no quotation is performed. A collection of predefined patterns can be found in
    /// the <see cref="Config.ExpressionQuotationPatterns"/> class. Any string is accepted, but for it
    /// to work correctly it must contain "{0}", which is where the expression will be inserted into the template.
    /// </summary>
    public static string? ExpressionQuotationPattern
    {
      get => _expressionQuotationPattern;
      set
      {
        if (value != null && !value.Contains("{0}"))
        {
          throw new ArgumentException("The provided pattern cannot be used as it does not contain a '{0}' inside of it.");
        }
        
        _expressionQuotationPattern = value;
      }
    }

    public delegate object? ValueRenderer(JsonPropertyInfo property, object obj, object? value);
    public delegate object ExceptionRenderer(JsonPropertyInfo property, object obj, Exception exception);
    public delegate bool ShouldIgnore(JsonPropertyInfo property, object obj, object? value);
    public delegate ExtraneousPropertiesOptions ExtraneousProperties(string propertyName, object? value);
    public delegate void LaunchDiffTool(string actual, string expected);
    public delegate string ExpectedFileDirectoryResolver(MethodInfo testMethod, FileInfo sourceFileLocation);
    public delegate bool PlaceholderValidator(string? value);

    public record CompareSnapshotsConfiguration
    {
      /// <summary>
      /// If a property throws an exception during serialization, this delegate can be used to customize the output, defaults to the exception message.
      /// </summary>
      public ExceptionRenderer ExceptionRenderer { get; set; } = (info, obj, exception) => exception.Message;
      
      /// <summary>
      /// Customize how the value of a property is rendered, defaults to the value itself. 
      /// </summary>
      public ValueRenderer? ValueRenderer { get; set; } 
      
      /// <summary>
      /// Given a property and its value, returns whether the property should be ignored and omitted from the output, defaults to false.
      /// </summary>
      public ShouldIgnore? ShouldIgnore { get; set; }
      
      /// <summary>
      /// Determines how extraneous properties should be handled (properties that exist on the actual object, but not in the expected object), defaults to <see cref="ExtraneousPropertiesOptions.Disallow"/>.
      /// </summary>
      public ExtraneousProperties? ExtraneousProperties { get; set; }
      
      /// <summary>
      /// A callback to launch a diff tool that gets executed when there is a difference between the actual and expected snapshots.
      /// </summary>
      public LaunchDiffTool? LaunchDiffTool { get; set; }
      
      /// <summary>
      /// Exclude null values from the output, defaults to false.
      /// </summary>
      public bool ExcludeNullValues { get; set; } = false;
      
      /// <summary>
      /// The prefix a properties placeholder should have in the expected snapshot, defaults to "@@".
      /// Any value with this prefix is considered a placeholder and the expected and actual values are not compared directly.
      /// </summary>
      public string PlaceholderPrefix { get; set; } = "@@";
      
      /// <summary>
      /// A callback to resolve the directory where the expected snapshot file should be located.
      /// </summary>
      public ExpectedFileDirectoryResolver? ExpectedFileDirectoryResolver { get; set; }

      internal Dictionary<string, (PlaceholderValidator, string)> PlaceholderValidators { get; } = new();

      /// <summary>
      /// Allows registering a placeholder validator for a specific placeholder that the actual value will be compared against to see if it is valid.
      /// </summary>
      /// <param name="placeholder">The placeholder without the prefix.</param>
      /// <param name="validator">A callback that is called for every encountered placeholder and is passed in the actual value as a string.</param>
      /// <param name="invalidValueMessage">The message that is returned when the value is not valid.</param>
      public void RegisterPlaceholderValidator(string placeholder, PlaceholderValidator validator, string invalidValueMessage)
      {
        PlaceholderValidators[placeholder] = (validator, invalidValueMessage);
      }

      public NormalizationConfiguration Normalization { get; set; } = new ();

      public record NormalizationConfiguration
      {
        /// <summary>
        /// Render DateTime and DateTimeOffset values as {DateTime} and {DateTimeOffset} respectively, defaults to true.
        /// </summary>
        public bool NormalizeDateTime { get; set; } = true;
        /// <summary>
        /// Render Guid values as {Guid}, defaults to true.
        /// </summary>
        public bool NormalizeGuid { get; set; } = true;
      }
      
      /// <summary>
      /// When the expected file does not exist, assume the actual value is correct and create the expected file as the actual value.
      /// This is useful for recreating expected files after you move or delete them. Defaults to false.
      /// </summary>
      public bool TreatAllSnapshotsAsCorrect { get; set; }

      private static readonly ConcurrentDictionary<object, JsonSerializerOptions> _jsonSerializerOptionsCache = new();
      
      internal JsonSerializerOptions GetJsonSerializerOptions()
      {
        // These properties affect the JsonSerializerOptions, so we use them as a key to cache the options.
        var key = new
        {
          Normalization.NormalizeGuid,
          Normalization.NormalizeDateTime,
          ExceptionRenderer,
          ExcludeNullValues,
          ValueRenderer,
          ShouldIgnore
        };
        
        if (_jsonSerializerOptionsCache.TryGetValue(key, out var options))
        {
          return options;
        }

        var jsonSerializerOptions = new JsonSerializerOptions()
        {
          TypeInfoResolver = new TypeInfoResolver(this),
          IncludeFields = true,
          WriteIndented = true,
          ReferenceHandler = ReferenceHandler.IgnoreCycles,
          AllowTrailingCommas = true,
          Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
          ReadCommentHandling = JsonCommentHandling.Skip
        };
        
        _jsonSerializerOptionsCache[key] = jsonSerializerOptions;
        
        return jsonSerializerOptions;
      }
    }

    public enum ExtraneousPropertiesOptions
    {
      Disallow,
      Ignore,
      AutomaticUpdate
    }
    
    public static CompareSnapshotsConfiguration Snapshots { get; } = new ();

    private static string? _expressionQuotationPattern;
  }
}