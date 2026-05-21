using System;
using System.Reflection;
using System.Threading;
using Xunit.v3;

namespace Assertive.xUnit;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EnableAssertiveSnapshotsAttribute : BeforeAfterTestAttribute
{
  private static readonly AsyncLocal<CurrentTestMethodInfo?> _currentMethod = new();

  public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
    _currentMethod.Value = new CurrentTestMethodInfo
    {
      State = new object(),
      Method = methodUnderTest
    };

  public override void After(MethodInfo methodUnderTest, IXunitTest test) =>
    _currentMethod.Value = null;

  public static CurrentTestMethodInfo? GetCurrentTestMethod() => _currentMethod.Value;
}

public class CurrentTestMethodInfo
{
  public required MethodInfo Method { get; set; }
  public required object State { get; set; }
}
