using System.Collections.Generic;
using System.Linq;
using Assertive.Interfaces;

namespace Assertive.Plugin
{
  /// <summary>
  /// Registry for DSL-defined patterns.
  /// </summary>
  internal static class CustomPatternRegistry
  {
    private static IFriendlyMessagePattern[] _patterns = [];
    private static readonly object _lock = new();

    internal static void Register(PatternDefinition definition)
    {
      lock (_lock)
      {
        _patterns = _patterns.Append(new CustomPattern(definition)).ToArray();
      }
    }

    internal static IFriendlyMessagePattern[] GetPatterns()
    {
      lock (_lock)
      {
        return _patterns.ToArray();
      }
    }

    internal static void Clear()
    {
      lock (_lock)
      {
        _patterns = [];
      }
    }
  }
}
