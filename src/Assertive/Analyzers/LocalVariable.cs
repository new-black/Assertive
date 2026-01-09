namespace Assertive.Analyzers
{
  /// <summary>
  /// Represents a local variable with its name and serialized value.
  /// </summary>
  internal record LocalVariable(string Name, string Value);
}
