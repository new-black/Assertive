using System.Collections.Generic;
using System.Linq;

namespace Assertive.Plugin
{
  /// <summary>
  /// Registry for DSL-defined patterns.
  /// </summary>
  internal static class CustomPatternRegistry
  {
    private static readonly Dictionary<string, PatternDefinition> _patterns = new();
    private static readonly object _lock = new();

    internal static void Register(string name, PatternDefinition definition)
    {
      lock (_lock)
      {
        // Upsert: replace existing pattern with the same name
        _patterns[name] = definition;
      }
    }

    internal static bool Unregister(string name)
    {
      lock (_lock)
      {
        return _patterns.Remove(name);
      }
    }

    /// <summary>Definitions in registration order, for the generated (probe-based) matcher.</summary>
    internal static List<PatternDefinition> GetDefinitions()
    {
      lock (_lock)
      {
        return _patterns.Values.ToList();
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
