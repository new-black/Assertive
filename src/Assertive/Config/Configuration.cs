using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

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

    public class CheckSettingsContainer
    {
      public Func<JsonPropertyInfo, Exception, object> ExceptionRenderer { get; set; } = (info, exception) => "Exception: " + exception.Message;
      public Func<JsonPropertyInfo, object, object?, object?>? ValueRenderer { get; set; }
      public Func<JsonPropertyInfo, object, object?, bool>? ShouldIgnore { get; set; }
      public Func<string, JsonNode?, ExtraneousPropertiesOptions>? ExtraneousPropertiesOption { get; set; }
      public Action<string, string>? LaunchDiffTool { get; set; }
    }

    public enum ExtraneousPropertiesOptions
    {
      Disallow,
      Ignore,
      AutomaticUpdate
    }
    
    public static CheckSettingsContainer CheckSettings { get; } = new ();

    private static string? _expressionQuotationPattern;
  }
}