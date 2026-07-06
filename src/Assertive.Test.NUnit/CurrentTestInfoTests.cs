using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assertive.TestFrameworks;
using static Assertive.DSL;

namespace Assertive.Test.NUnit;

[TestFixture]
public class SnapshotCounterTests
{
  private static CurrentTestInfo MakeInfo(object state) => new()
  {
    State = state,
    Name = "test",
    ClassName = "cls",
    Method = typeof(SnapshotCounterTests).GetMethod(nameof(Counter_is_independent_per_state_object))!,
    Arguments = []
  };

  [Test]
  public void Counter_is_independent_per_state_object()
  {
    var stateA = new object();
    var stateB = new object();

    // Attempt 1 (state = A): counter starts at 1
    var a1 = AssertImpl.UpdateState(MakeInfo(stateA), "expr");
    Assert(() => a1.GetCounter("expr") == 1);

    // Retry (state = B, simulating a fresh context): counter resets to 1
    var b1 = AssertImpl.UpdateState(MakeInfo(stateB), "expr");
    Assert(() => b1.GetCounter("expr") == 1);

    // Further calls on A keep counting from where A left off
    var a2 = AssertImpl.UpdateState(MakeInfo(stateA), "expr");
    Assert(() => a2.GetCounter("expr") == 2);
  }
}

public class CurrentTestInfoTests
{
  [Test]
  public void Can_get_current_test_info()
  {
    var testFramework = new NUnitTestFramework();
    var currentTestInfo = testFramework.GetCurrentTestInfo();

    var method = this.GetType().GetMethod(nameof(Can_get_current_test_info), BindingFlags.Instance | BindingFlags.Public);

    Assert(() => currentTestInfo.Name == nameof(Can_get_current_test_info)
                 && currentTestInfo.Method == method
                 && currentTestInfo.Name == TestContext.CurrentContext.Test.Name
                 && currentTestInfo.ClassName == TestContext.CurrentContext.Test.ClassName
                 && currentTestInfo.Arguments.Length == 0);
  }
  
  [TestCase("arg1", "arg2", 3, 4.0)]
  [TestCase(null, "", 3, 4.0)]
  public void Can_get_current_test_info_with_arguments(string? a, string b, int c, double d)
  {
    var testFramework = new NUnitTestFramework();
    var currentTestInfo = testFramework.GetCurrentTestInfo();

    var method = this.GetType().GetMethod(nameof(Can_get_current_test_info_with_arguments), BindingFlags.Instance | BindingFlags.Public);

    Assert(() => currentTestInfo.Name == """Can_get_current_test_info_with_arguments"""
                 && currentTestInfo.Name == TestContext.CurrentContext.Test.MethodName
                 && currentTestInfo.ClassName == TestContext.CurrentContext.Test.ClassName
                 && currentTestInfo.Method == method
                 && (string)currentTestInfo.Arguments[0] == a
                 && (string)currentTestInfo.Arguments[1] == b
                 && (int)currentTestInfo.Arguments[2] == c
                 && (double)currentTestInfo.Arguments[3] == d);
  }
}

[TestFixture]
public class ParameterizedStateTests
{
  // The snapshot counter is keyed by State. When a test framework retries a failing
  // test (e.g. [RetryOnError]), NUnit reuses the same Test object but creates a fresh
  // TestExecutionContext for each attempt. If State were the Test object the counter
  // would persist across retries, causing snapshot #N+1 to be requested on attempt N+1
  // (which doesn't exist), turning a simple mismatch into a cascade of "no snapshot"
  // failures. State must therefore be the TestExecutionContext, not the Test object.
  [Test]
  public void State_is_the_execution_context_so_retries_get_a_fresh_counter()
  {
    var testFramework = new NUnitTestFramework();
    var info = testFramework.GetCurrentTestInfo()!;

    var contextType = Type.GetType("NUnit.Framework.Internal.TestExecutionContext, nunit.framework");
    var currentContext = contextType?.GetProperty("CurrentContext")?.GetValue(null);

    Assert(() => currentContext != null);
    Assert(() => ReferenceEquals(info.State, currentContext));
  }
}