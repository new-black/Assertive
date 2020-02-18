using System;
using System.Linq;

namespace Assertive.Frameworks
{
  internal static class TestFrameworkHelper
  {
    public static Type? TryGetExceptionType(string assemblyName, string typeName)
    {
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();

      var xunit = assemblies.FirstOrDefault(a => a.FullName.StartsWith(assemblyName + ",", StringComparison.OrdinalIgnoreCase));

      if (xunit != null)
      {
        var type = xunit.GetType(typeName);

        if (type != null)
        {
          return type;
        }
      }

      return null;
    }
  }
}