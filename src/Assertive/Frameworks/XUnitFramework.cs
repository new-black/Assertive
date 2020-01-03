using System;
using System.Linq;

namespace Assertive.Frameworks
{
  internal class XUnitFramework : ITestFramework
  {
    private Type _exceptionType = null;
    
    public bool IsAvailable
    {
      get
      {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var xunit = assemblies.FirstOrDefault(a => a.FullName.StartsWith("xunit.assert,", StringComparison.OrdinalIgnoreCase));

        if (xunit != null)
        {
          var type = xunit.GetType("Xunit.Sdk.XunitException");

          if (type != null)
          {
            _exceptionType = type;
            return true;
          }
        }

        return false;
      }
    }

    public Type ExceptionType => _exceptionType;
  }
}