using System;
using System.Reflection;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class XUnitFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    private Type? _enableAssertType = null;
    private MethodInfo? _getCurrentTestMethodInfo = null;

    public Type? ExceptionType
    {
      get
      {
        // XunitException lives in Xunit.Sdk in both v2 and v3, but the assembly differs:
        // v2 → xunit.assert, v3 → xunit.v3.assert.
        return _exceptionType ??=
          TestFrameworkHelper.TryGetType("xunit.v3.assert", "Xunit.Sdk.XunitException", "xunit.v3")
          ?? TestFrameworkHelper.TryGetType("xunit.assert", "Xunit.Sdk.XunitException", "xunit");
      }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Best-effort reflection to detect the running xUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort reflection to detect the running xUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2080", Justification = "Best-effort reflection to detect the running xUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    public CurrentTestInfo? GetCurrentTestInfo()
    {
      // Assertive.xUnit.v3 (xUnit v3) and Assertive.xUnit (xUnit v2) both expose the same
      // Assertive.xUnit.EnableAssertiveSnapshotsAttribute type with a static GetCurrentTestMethod method.
      var attribute = _enableAssertType ??=
        TestFrameworkHelper.TryGetType("Assertive.xUnit.v3", "Assertive.xUnit.EnableAssertiveSnapshotsAttribute")
        ?? TestFrameworkHelper.TryGetType("Assertive.xUnit", "Assertive.xUnit.EnableAssertiveSnapshotsAttribute");

      if (attribute == null)
      {
        return null;
      }

      var method = _getCurrentTestMethodInfo ??= attribute.GetMethod("GetCurrentTestMethod", BindingFlags.Public | BindingFlags.Static);

      if (method == null)
      {
        return null;
      }

      dynamic? currentTestMethod = method.Invoke(null, null);

      if (currentTestMethod == null)
      {
        return null;
      }

      var methodInfo = currentTestMethod.Method as MethodInfo;

      if (methodInfo == null || methodInfo.DeclaringType?.FullName == null)
      {
        return null;
      }

      return new CurrentTestInfo()
      {
        Method = methodInfo,
        Name = methodInfo.Name,
        Arguments = [],
        State = currentTestMethod.State,
        ClassName = methodInfo.DeclaringType.FullName
      };
    }
  }
}
