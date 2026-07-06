using System;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class NUnitTestFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    private Type? _testContextType = null;
    
    public Type? ExceptionType
    {
      get
      {
        return _exceptionType ??= TestFrameworkHelper.TryGetType("nunit.framework", "NUnit.Framework.AssertionException");
      }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Best-effort reflection to detect the running NUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Best-effort reflection to detect the running NUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort reflection to detect the running NUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2080", Justification = "Best-effort reflection to detect the running NUnit test; if the metadata is trimmed, detection returns null and snapshot file naming degrades gracefully.")]
    public CurrentTestInfo? GetCurrentTestInfo()
    {
      var testContextType = _testContextType ??= TestFrameworkHelper.TryGetType("nunit.framework", "NUnit.Framework.Internal.TestExecutionContext");

      dynamic? currentTest = testContextType?.GetProperty("CurrentContext")?.GetValue(null);
      
      if (currentTest == null)
      {
        return null;
      }
      
      var test = currentTest.CurrentTest;

      return new CurrentTestInfo()
      {
        Method = test.Method.MethodInfo,
        Name = test.MethodName,
        ClassName = test.ClassName,
        Arguments = test.Arguments,
        State = currentTest
      };
    }
  }
}