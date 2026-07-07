using System.Collections.Generic;
using System.Linq;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;
using Assertive.TestFrameworks;
using static Assertive.DSL;

namespace Assertive.Test.NUnit;

public class ProbeRetryCommand : DelegatingTestCommand
{
  private readonly int _tryCount;
  public ProbeRetryCommand(TestCommand innerCommand, int tryCount) : base(innerCommand) => _tryCount = tryCount;

  public override TestResult Execute(TestExecutionContext context)
  {
    var count = _tryCount;
    while (count-- > 0)
    {
      try
      {
        context.CurrentResult = innerCommand.Execute(context);
      }
      catch (System.Exception ex)
      {
        context.CurrentResult ??= context.CurrentTest.MakeTestResult();
        context.CurrentResult.RecordException(ex);
      }

      if (context.CurrentResult.ResultState != ResultState.Failure &&
          context.CurrentResult.ResultState != ResultState.Error)
      {
        break;
      }
      if (count > 0)
      {
        context.CurrentResult = context.CurrentTest.MakeTestResult();
        context.CurrentRepeatCount++;
      }
    }
    return context.CurrentResult;
  }
}

public class ProbeRetryAttribute : NUnitAttribute, IRepeatTest
{
  private readonly int _tryCount;
  public ProbeRetryAttribute(int tryCount) => _tryCount = tryCount;
  public TestCommand Wrap(TestCommand command) => new ProbeRetryCommand(command, _tryCount);
}

[TestFixture]
public class SnapshotCounterRetryTests
{
  private static readonly List<object> _states = new();

  [OneTimeSetUp]
  public void Reset() { _states.Clear(); }

  // Mirrors EVA's [RetryOnError]: an async test that fails on the first attempts, is retried
  // through a DelegatingTestCommand that increments CurrentRepeatCount, and takes a snapshot-
  // counter key (State) late in the body after awaits. Each retry must get a fresh State so the
  // snapshot counter does not climb (#expr_1 -> #expr_2 -> ...) across attempts and request
  // snapshots that don't exist. The verification runs on the final attempt so a regression
  // fails the test itself (a OneTimeTearDown failure does not fail the run).
  [Test]
  [ProbeRetry(3)]
  public async System.Threading.Tasks.Task State_resets_across_retries_of_the_same_test()
  {
    await System.Threading.Tasks.Task.Yield();
    await System.Threading.Tasks.Task.Delay(1);

    _states.Add(new NUnitTestFramework().GetCurrentTestInfo()!.State);

    // Fail the first two attempts to force two retries.
    if (_states.Count < 3)
    {
      throw new System.Exception("forced failure to trigger retry");
    }

    // Final attempt: every retry must have been handed a distinct State reference; that is what
    // keeps the per-test snapshot counter from bleeding across retries.
    Assert(() => _states.Distinct(ReferenceEqualityComparer.Instance).Count() == 3);
  }
}
