using System;
using System.Linq;

namespace Assertive.Helpers
{
  internal static class TestFrameworkHelper
  {
    public static Type? TryGetExceptionType(string assemblyName, string typeName)
    {
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();

      var assembly = assemblies.FirstOrDefault(a => a.FullName.StartsWith(assemblyName + ",", StringComparison.OrdinalIgnoreCase));

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