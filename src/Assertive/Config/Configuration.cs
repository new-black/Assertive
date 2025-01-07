using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Assertive.TestFrameworks;

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

    public record CompareSnapshotsConfiguration
    {
      public Func<JsonPropertyInfo, Exception, object> ExceptionRenderer { get; set; } = (info, exception) => exception.Message;
      public Func<JsonPropertyInfo, object?, object?>? ValueRenderer { get; set; } 
      public Func<JsonPropertyInfo, object, object?, bool>? ShouldIgnore { get; set; }
      public Func<string, JsonNode?, ExtraneousPropertiesOptions>? ExtraneousProperties { get; set; }
      public Action<string, string>? LaunchDiffTool { get; set; }
      public bool ExcludeNullValues { get; set; } = false;
      public string PlaceholderPrefix { get; set; } = "@@";
      
      public Func<MethodInfo, FileInfo, string>? ExpectedFileDirectoryResolver { get; set; }

      internal Dictionary<string, (Func<string?, bool>, string)> PlaceholderValidators { get; } = new();

      public void RegisterPlaceholderValidator(string placeholder, Func<string?, bool> validator, string invalidValueMessage)
      {
        PlaceholderValidators[placeholder] = (validator, invalidValueMessage);
      }

      public NormalizationConfiguration Normalization { get; } = new ();

      public class NormalizationConfiguration
      {
        public bool NormalizeDateTime { get; set; } = true;
        public bool NormalizeGuid { get; set; } = true;
      }

      internal JsonSerializerOptions JsonSerializerOptions { get; }

      public CompareSnapshotsConfiguration()
      {
        JsonSerializerOptions = new JsonSerializerOptions()
        {
          TypeInfoResolver = new TypeInfoResolver(this),
          IncludeFields = true,
          WriteIndented = true,
          ReferenceHandler = ReferenceHandler.IgnoreCycles,
          AllowTrailingCommas = true,
          Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
          ReadCommentHandling = JsonCommentHandling.Skip
        };
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