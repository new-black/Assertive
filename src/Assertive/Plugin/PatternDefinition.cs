namespace Assertive.Plugin
{
  /// <summary>
  /// Root definition for a custom assertion pattern.
  /// </summary>
  public class PatternDefinition
  {
    /// <summary>
    /// Array of predicates that must ALL match (AND logic).
    /// </summary>
    public MatchPredicate[] Match { get; set; } = [];

    /// <summary>
    /// Whether this pattern can be negated with the ! operator.
    /// </summary>
    public bool AllowNegation { get; set; }

    /// <summary>
    /// Output when the pattern matches (non-negated case).
    /// </summary>
    public OutputDefinition? Output { get; set; }

    /// <summary>
    /// Output when the pattern matches and is negated.
    /// Only used if AllowNegation is true.
    /// </summary>
    public OutputDefinition? OutputWhenNegated { get; set; }
  }

  /// <summary>
  /// A single match predicate. Multiple predicates in the match array are AND'd together.
  /// </summary>
  public class MatchPredicate
  {
    /// <summary>
    /// Match a method call.
    /// </summary>
    public MethodMatch? Method { get; set; }

    /// <summary>
    /// Match a property access.
    /// </summary>
    public PropertyMatch? Property { get; set; }

    /// <summary>
    /// Match against the declaring type of the method or property.
    /// </summary>
    public string? DeclaringType { get; set; }

    /// <summary>
    /// Match against the namespace of the declaring type.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Match against the type of the instance the method/property is called on.
    /// </summary>
    public string? InstanceType { get; set; }
  }

  /// <summary>
  /// Criteria for matching a method call.
  /// </summary>
  public class MethodMatch
  {
    /// <summary>
    /// The exact method name to match.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Match methods with this exact parameter count.
    /// </summary>
    public int? ParameterCount { get; set; }

    /// <summary>
    /// Match only extension methods (true) or only non-extension methods (false).
    /// </summary>
    public bool? IsExtension { get; set; }
  }

  /// <summary>
  /// Criteria for matching a property access.
  /// </summary>
  public class PropertyMatch
  {
    /// <summary>
    /// The exact property name to match.
    /// </summary>
    public string? Name { get; set; }
  }

  /// <summary>
  /// Defines the expected/actual output messages.
  /// </summary>
  public class OutputDefinition
  {
    /// <summary>
    /// Template for the "expected" message.
    /// Supports placeholders like {instance}, {instance.count}, {arg0}, etc.
    /// </summary>
    public string? Expected { get; set; }

    /// <summary>
    /// Template for the "actual" message.
    /// Supports placeholders like {instance}, {instance.count}, {arg0}, etc.
    /// </summary>
    public string? Actual { get; set; }
  }
}
