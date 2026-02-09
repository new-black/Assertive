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
  /// <summary>
  /// Global configuration settings for Assertive assertions.
  /// </summary>
  public static partial class Configuration
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

    /// <summary>
    /// Delegate for customizing how property values are rendered in snapshot output.
    /// </summary>
    /// <param name="property">The property being serialized.</param>
    /// <param name="obj">The object containing the property.</param>
    /// <param name="value">The current value of the property.</param>
    /// <returns>The value to render (return the input value for no change, null to insert null).</returns>
    public delegate object? ValueRenderer(JsonPropertyInfo property, object obj, object? value);

    /// <summary>
    /// Delegate for customizing how exceptions are rendered when property access fails during serialization.
    /// </summary>
    /// <param name="property">The property that threw the exception.</param>
    /// <param name="obj">The object containing the property.</param>
    /// <param name="exception">The exception that was thrown.</param>
    /// <returns>The value to render in place of the exception.</returns>
    public delegate object ExceptionRenderer(JsonPropertyInfo property, object obj, Exception exception);

    /// <summary>
    /// Delegate for determining whether a property should be excluded from snapshot output.
    /// </summary>
    /// <param name="property">The property being considered.</param>
    /// <param name="obj">The object containing the property.</param>
    /// <param name="value">The current value of the property.</param>
    /// <returns>True to exclude the property from output, false to include it.</returns>
    public delegate bool ShouldIgnore(JsonPropertyInfo property, object obj, object? value);

    /// <summary>
    /// Delegate for determining how to handle properties in the actual object that don't exist in the expected snapshot.
    /// </summary>
    /// <param name="propertyName">The name of the extraneous property.</param>
    /// <param name="value">The value of the extraneous property.</param>
    /// <returns>How to handle the extraneous property.</returns>
    public delegate ExtraneousPropertiesOptions ExtraneousProperties(string propertyName, object? value);

    /// <summary>
    /// Delegate for launching an external diff tool when snapshots differ.
    /// </summary>
    /// <param name="actual">Path to the file containing the actual value.</param>
    /// <param name="expected">Path to the file containing the expected value.</param>
    public delegate void LaunchDiffTool(string actual, string expected);

    /// <summary>
    /// Delegate for resolving the directory where expected snapshot files should be stored.
    /// </summary>
    /// <param name="testMethod">The test method performing the snapshot assertion.</param>
    /// <param name="sourceFileLocation">The source file containing the test.</param>
    /// <returns>The directory path where the expected snapshot file should be located.</returns>
    public delegate string ExpectedFileDirectoryResolver(MethodInfo testMethod, FileInfo sourceFileLocation);

    /// <summary>
    /// Delegate for validating placeholder values in expected snapshots.
    /// </summary>
    /// <param name="value">The actual value to validate against the placeholder.</param>
    /// <returns>True if the value is valid for the placeholder, false otherwise.</returns>
    public delegate bool PlaceholderValidator(string? value);

    /// <summary>
    /// Configuration settings for snapshot comparison assertions.
    /// </summary>
    public record CompareSnapshotsConfiguration
    {
      /// <summary>
      /// If a property throws an exception during serialization, this delegate can be used to customize the output, defaults to the exception message.
      /// </summary>
      public ExceptionRenderer ExceptionRenderer { get; set; } = (info, obj, exception) => exception.Message;
      
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
      /// The diff tool will NOT be launched when detected to be running in LLM/AI coding assistant contexts (Claude Code, Cursor, Aider, Continue.dev)
      /// or CI/automated environments (CI, GITHUB_ACTIONS, TF_BUILD, JENKINS_URL, TEAMCITY_VERSION).
      /// To disable explicitly set ASSERTIVE_DISABLE_DIFF_TOOL=1.
      /// </summary>
      public LaunchDiffTool? LaunchDiffTool { get; set; }
      
      /// <summary>
      /// Exclude null values from the output, defaults to false.
      /// </summary>
      public bool ExcludeNullValues { get; set; } = false;
      
      /// <summary>
      /// A callback to resolve the directory where the expected snapshot file should be located.
      /// </summary>
      public ExpectedFileDirectoryResolver? ExpectedFileDirectoryResolver { get; set; }

      /// <summary>
      /// Settings for normalizing values during snapshot comparison.
      /// </summary>
      public NormalizationConfiguration Normalization { get; set; } = new ();

      /// <summary>
      /// Configuration for value normalization during snapshot comparison.
      /// </summary>
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
        
        /// <summary>
        /// Customize how the value of a property is rendered, defaults to the value itself.
        /// If you don't want to alter the value, return the value passed into the method, returning null will insert null into the output.
        /// </summary>
        public ValueRenderer? ValueRenderer { get; set; } 

        /// <summary>
        /// The prefix a properties placeholder should have in the expected snapshot, defaults to "@@".
        /// Any value with this prefix is considered a placeholder and the expected and actual values are not compared directly.
        /// </summary>
        public string PlaceholderPrefix { get; set; } = "@@";
        
        internal Dictionary<string, (PlaceholderValidator, string)> PlaceholderValidatorsLookup { get; } = new();

        /// <summary>
        /// Gets or sets the registered placeholder validators.
        /// </summary>
        public IEnumerable<PlaceholderValidatorDefinition> PlaceholderValidators
        {
          get
          {
            foreach (var (placeholder, (validator, invalidValueMessage)) in PlaceholderValidatorsLookup)
            {
              yield return new PlaceholderValidatorDefinition(placeholder, validator, invalidValueMessage);
            }
          }
          set
          {
            foreach (var (placeholder, validator, invalidValueMessage) in value)
            {
              PlaceholderValidatorsLookup[placeholder] = (validator, invalidValueMessage);
            }
          }
        }

        /// <summary>
        /// Allows registering a placeholder validator for a specific placeholder that the actual value will be compared against to see if it is valid.
        /// </summary>
        /// <param name="placeholder">The placeholder without the prefix.</param>
        /// <param name="validator">A callback that is called for every encountered placeholder and is passed in the actual value as a string.</param>
        /// <param name="invalidValueMessage">The message that is returned when the value is not valid.</param>
        public void RegisterPlaceholderValidator(string placeholder, PlaceholderValidator validator, string invalidValueMessage)
        {
          PlaceholderValidatorsLookup[placeholder] = (validator, invalidValueMessage);
        }
      }
      
      /// <summary>
      /// Defines a placeholder validator with its associated validation message.
      /// </summary>
      /// <param name="Placeholder">The placeholder name without the prefix.</param>
      /// <param name="Validator">The validation function.</param>
      /// <param name="InvalidValueMessage">The message shown when validation fails.</param>
      public record PlaceholderValidatorDefinition(string Placeholder, PlaceholderValidator Validator, string InvalidValueMessage);
      
      /// <summary>
      /// When the expected file does not exist, assume the actual value is correct and create the expected file as the actual value.
      /// This is useful for recreating expected files after you move or delete them. Defaults to false.
      /// </summary>
      public bool TreatAllSnapshotsAsCorrect { get; set; }

      /// <summary>
      /// When true, new snapshots (where no expected file exists) are automatically accepted and the test passes.
      /// Unlike <see cref="TreatAllSnapshotsAsCorrect"/>, this only affects new snapshots and won't overwrite existing ones.
      /// Useful for AI agents and automated workflows where you want to generate snapshots on first run.
      /// Defaults to false.
      /// </summary>
      public bool AcceptNewSnapshots { get; set; }

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
          Normalization.ValueRenderer,
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

    /// <summary>
    /// Specifies how to handle properties in the actual object that don't exist in the expected snapshot.
    /// </summary>
    public enum ExtraneousPropertiesOptions
    {
      /// <summary>Fail the assertion if extraneous properties are found.</summary>
      Disallow,
      /// <summary>Ignore extraneous properties and don't include them in comparison.</summary>
      Ignore,
      /// <summary>Automatically add extraneous properties to the expected snapshot file.</summary>
      AutomaticUpdate
    }
    
    /// <summary>
    /// Global configuration for snapshot assertions.
    /// </summary>
    public static CompareSnapshotsConfiguration Snapshots { get; } = new ();

    /// <summary>
    /// Color scheme configuration for assertion failure messages.
    /// Set <see cref="ColorScheme.Enabled"/> to false to disable all colorization.
    /// </summary>
    public static ColorScheme Colors { get; } = new ();

    /// <summary>
    /// Output formatting configuration for assertion failure messages.
    /// </summary>
    public static OutputConfiguration Output { get; } = new();

    /// <summary>
    /// Configuration for assertion failure message output formatting.
    /// </summary>
    public class OutputConfiguration
    {
      /// <summary>
      /// Maximum number of characters to display for serialized values in Expected/Actual output.
      /// When a value exceeds this length, it will be truncated with "..." appended.
      /// Set to null (default) for unlimited output.
      /// </summary>
      /// <example>
      /// <code>
      /// // Limit output to 500 characters
      /// Configuration.Output.MaxValueLength = 500;
      ///
      /// // Unlimited output (default)
      /// Configuration.Output.MaxValueLength = null;
      /// </code>
      /// </example>
      public int? MaxValueLength { get; set; }
    }

    private static string? _expressionQuotationPattern;
  }
}