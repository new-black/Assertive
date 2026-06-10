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
    private static readonly Dictionary<string, (PatternDefinition Definition, IFriendlyMessagePattern Pattern)> _patterns = new();
    private static readonly object _lock = new();

    internal static void Register(string name, PatternDefinition definition)
    {
      lock (_lock)
      {
        // Upsert: replace existing pattern with the same name
        _patterns[name] = (definition, new CustomPattern(definition));
      }
    }

    internal static bool Unregister(string name)
    {
      lock (_lock)
      {
        return _patterns.Remove(name);
      }
    }

    internal static ICollection<IFriendlyMessagePattern> GetPatterns()
    {
      lock (_lock)
      {
        return _patterns.Values.Select(p => p.Pattern).ToList();
      }
    }

    /// <summary>Definitions in registration order, for the generated (probe-based) matcher.</summary>
    internal static List<PatternDefinition> GetDefinitions()
    {
      lock (_lock)
      {
        return _patterns.Values.Select(p => p.Definition).ToList();
      }
    }

    internal static bool IsEmpty
    {
      get
      {
        lock (_lock)
        {
          return _patterns.Count == 0;
        }
      }
    }

    internal static void Clear()
    {
      lock (_lock)
      {
        _patterns.Clear();
      }
    }
  }
}
