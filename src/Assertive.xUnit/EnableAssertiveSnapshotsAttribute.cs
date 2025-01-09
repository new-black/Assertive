using System;
using System.Reflection;
using System.Threading;
using Xunit.Sdk;

namespace Assertive.xUnit;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EnableAssertiveSnapshotsAttribute : BeforeAfterTestAttribute
{
  private static readonly AsyncLocal<CurrentTestMethodInfo?> _currentMethod = new();

  public override void Before(MethodInfo method) =>
    _currentMethod.Value = new CurrentTestMethodInfo
    {
      State = new object(),
      Method = method
    };

  public override void After(MethodInfo method) =>
    _currentMethod.Value = null;

  public static CurrentTestMethodInfo? GetCurrentTestMethod() => _currentMethod.Value;
}

public class CurrentTestMethodInfo
{
  public required MethodInfo Method { get; set; }
  public required object State { get; set; }
}