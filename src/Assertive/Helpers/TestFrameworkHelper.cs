using System;
using System.Linq;
using System.Reflection;

namespace Assertive.Helpers
{
  internal static class TestFrameworkHelper
  {
    public static Type? TryGetType(string assemblyName, string typeName, string? assemblyPrefix = null)
    {
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();

      var assembly = assemblies.FirstOrDefault(a => a.FullName?.StartsWith(assemblyName + ",", StringComparison.OrdinalIgnoreCase) == true);

      if (assembly == null && assemblyPrefix != null)
      {
        var frameworkLoadedAtAll =
          assemblies.Any(a => a.FullName?.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase) == true);

        // In case of xUnit the assertion DLL is not loaded if you don't use any xUnit assertions
        // so check if we can find any assemblies that match the prefix and if so make an attempt
        // to load the assembly.
        if (frameworkLoadedAtAll)
        {
          try
          {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
          }
          catch
          {
            // ignored
          }
        }
      }

      if (assembly != null)
      {
        var type = assembly.GetType(typeName);

        if (type != null)
        {
          return type;
        }
      }

      return null;
    }
  }
}