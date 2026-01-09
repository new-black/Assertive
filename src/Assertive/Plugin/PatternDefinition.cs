namespace Assertive.Plugin
{
  /// <summary>
  /// Defines a custom assertion pattern that provides friendly error messages
  /// for specific method calls or property accesses.
  /// </summary>
  public class PatternDefinition
  {
    /// <summary>
    /// Predicates that must all match for this pattern to apply (AND logic).
    /// </summary>
    public MatchPredicate[] Match { get; set; } = [];

    /// <summary>
    /// Whether this pattern matches negated assertions (e.g., <c>!list.None()</c>).
    /// When true, <see cref="OutputWhenNegated"/> must also be provided.
    /// </summary>
    public bool AllowNegation { get; set; }

    /// <summary>
    /// The message templates to use when the assertion fails.
    /// </summary>
    public OutputDefinition? Output { get; set; }

    /// <summary>
    /// The message templates to use when a negated assertion fails.
    /// Required when <see cref="AllowNegation"/> is true.
    /// </summary>
    public OutputDefinition? OutputWhenNegated { get; set; }
  }

  /// <summary>
  /// A predicate that constrains which expressions a pattern matches.
  /// Multiple predicates in a pattern are combined with AND logic.
  /// </summary>
  public class MatchPredicate
  {
    /// <summary>
    /// Matches method calls. Set properties on this object to constrain which methods match.
    /// </summary>
    public MethodMatch? Method { get; set; }

    /// <summary>
    /// Matches property accesses. Set properties on this object to constrain which properties match.
    /// </summary>
    public PropertyMatch? Property { get; set; }

    /// <summary>
    /// Matches by the declaring type name (simple name or full name).
    /// </summary>
    public string? DeclaringType { get; set; }

    /// <summary>
    /// Matches by the namespace of the declaring type (exact match or prefix).
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Matches by the type of the instance the method or property is called on.
    /// For generic types, the simple name without type parameters matches (e.g., "List" matches <c>List&lt;T&gt;</c>).
    /// </summary>
    public string? InstanceType { get; set; }
  }

  /// <summary>
  /// Constraints for matching method calls.
  /// </summary>
  public class MethodMatch
  {
    /// <summary>
    /// The exact method name to match.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The number of parameters the method must have (excluding the instance for extension methods).
    /// </summary>
    public int? ParameterCount { get; set; }

    /// <summary>
    /// If true, only matches extension methods. If false, only matches non-extension methods.
    /// </summary>
    public bool? IsExtension { get; set; }
  }

  /// <summary>
  /// Constraints for matching property accesses.
  /// </summary>
  public class PropertyMatch
  {
    /// <summary>
    /// The exact property name to match.
    /// </summary>
    public string? Name { get; set; }
  }

  /// <summary>
  /// Message templates for assertion failure output.
  /// Templates support placeholders that are replaced with values at runtime.
  /// </summary>
  /// <remarks>
  /// Available placeholders:
  /// <list type="bullet">
  ///   <item><c>{instance}</c> - the expression the method/property was called on</item>
  ///   <item><c>{instance.value}</c> - the evaluated value of the instance</item>
  ///   <item><c>{instance.type}</c> - the type name of the instance</item>
  ///   <item><c>{instance.count}</c> - the item count if the instance is a collection</item>
  ///   <item><c>{instance.firstTenItems}</c> - the first 10 items of a collection</item>
  ///   <item><c>{arg0}</c>, <c>{arg1}</c>, etc. - method argument expressions</item>
  ///   <item><c>{arg0.value}</c>, <c>{arg1.value}</c>, etc. - evaluated argument values</item>
  ///   <item><c>{arg0.type}</c>, <c>{arg1.type}</c>, etc. - argument type names</item>
  ///   <item><c>{method}</c> - the method name</item>
  ///   <item><c>{property}</c> - the property name (for property patterns)</item>
  ///   <item><c>{value}</c> - the property value (for property patterns)</item>
  /// </list>
  /// </remarks>
  public class OutputDefinition
  {
    /// <summary>
    /// Template for the "expected" portion of the failure message.
    /// </summary>
    public string? Expected { get; set; }

    /// <summary>
    /// Template for the "actual" portion of the failure message.
    /// </summary>
    public string? Actual { get; set; }
  }
}
