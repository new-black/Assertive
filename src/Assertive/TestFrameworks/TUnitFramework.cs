using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Assertive.Helpers;

namespace Assertive.TestFrameworks
{
  internal class TUnitFramework : ITestFramework
  {
    private Type? _exceptionType;
    private Type? _testContextType;

    public Type? ExceptionType =>
      _exceptionType ??= TestFrameworkHelper.TryGetType("TUnit.Assertions", "TUnit.Assertions.Exceptions.AssertionException", "TUnit");

    public CurrentTestInfo? GetCurrentTestInfo()
    {
      var testContextType = _testContextType ??= TestFrameworkHelper.TryGetType("TUnit.Core", "TUnit.Core.TestContext", "TUnit");
      if (testContextType == null)
      {
        return null;
      }

      dynamic? current = testContextType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

      if (current == null) return null;

      var metadata = current.Metadata;
      
      var testDetails = metadata?.GetType().GetProperty("TestDetails", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(metadata);

      var classType = (Type?)testDetails?.GetType().GetProperty("ClassType", BindingFlags.Public | BindingFlags.Instance)?.GetValue(testDetails);
      var methodName = testDetails?.GetType().GetProperty("MethodName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(testDetails) as string;
      var args = (object[]?)testDetails?.GetType().GetProperty("TestMethodArguments", BindingFlags.Public | BindingFlags.Instance)?.GetValue(testDetails) ?? Array.Empty<object>();
      var displayName = testDetails?.GetType().GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(testDetails) as string
                       ?? testDetails?.GetType().GetProperty("TestName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(testDetails) as string
                       ?? methodName;

      MethodInfo? methodInfo = null;
      if (classType != null && methodName != null)
      {
        var allCandidates = classType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
          .Where(m => m.Name == methodName)
          .ToArray();

        if (args.Length > 0)
        {
          methodInfo = allCandidates.FirstOrDefault(m => m.GetParameters().Length == args.Length);
        }

        methodInfo ??= allCandidates.FirstOrDefault();
      }

      if (methodInfo == null || classType == null || methodName == null)
      {
        return null;
      }

      return new CurrentTestInfo
      {
        Method = methodInfo,
        Name = displayName ?? methodName,
        ClassName = classType.FullName ?? "Unknown",
        Arguments = args,
        State = testDetails ?? current!
      };
    }
  }
}
