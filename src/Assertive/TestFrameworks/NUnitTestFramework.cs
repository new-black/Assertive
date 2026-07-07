using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class NUnitTestFramework : ITestFramework
  {
    private Type? _exceptionType = null;
    private Type? _testContextType = null;

    // Maps (TestExecutionContext, repeatCount) → a stable reference-type key for AssertionState.
    // ConditionalWeakTable keeps weak refs to the context, so entries are collected automatically.
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<int, object>> _repeatKeyCache = new();
    
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
      var repeatCount = (int)currentTest.CurrentRepeatCount;
      var repeatKeys = _repeatKeyCache.GetValue((object)currentTest, static _ => new ConcurrentDictionary<int, object>());
      var stateKey = repeatKeys.GetOrAdd(repeatCount, static _ => new object());

      return new CurrentTestInfo()
      {
        Method = test.Method.MethodInfo,
        Name = test.MethodName,
        ClassName = test.ClassName,
        Arguments = test.Arguments,
        State = stateKey
      };
    }
  }
}