using System;
using System.ComponentModel;

namespace Assertive.Runtime
{
  /// <summary>
  /// Structural facts about an assertion body whose root is a method call or property
  /// access, recorded by the source generator so runtime-registered custom patterns
  /// (Configuration.Patterns) can match and render their templates — the generated
  /// equivalent of CustomPattern matching over expression trees. Infrastructure for
  /// generated code; not intended to be used directly.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class CustomPatternProbe
  {
    /// <summary>True for a method call root, false for a property access root.</summary>
    public bool IsMethodCall;

    /// <summary>Whether the assertion body was wrapped in a logical negation.</summary>
    public bool Negated;

    /// <summary>The method or property name.</summary>
    public string? MemberName;

    /// <summary>Declared parameter count, excluding the receiver for extension methods.</summary>
    public int ParameterCount;

    public bool IsExtension;

    public Type? DeclaringType;

    /// <summary>The static type of the instance/receiver expression.</summary>
    public Type? InstanceStaticType;

    public string? InstanceSource;
    public Func<object?>? Instance;

    public string?[]? ArgSources;
    public Func<object?>?[]? Args;
    public Type?[]? ArgStaticTypes;

    /// <summary>Property roots: evaluates the property value.</summary>
    public Func<object?>? Value;
  }
}
