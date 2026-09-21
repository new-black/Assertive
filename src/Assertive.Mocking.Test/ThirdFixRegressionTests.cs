using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

// ---------------------------------------------------------------------------
// Regression tests for the third fix pass on PR #12 (tight re-review of 5395454).
//
// BLOCKER (M2 reintroduced): a matcher whose call is not intercepted (here: a matcher stored in a
// local, then passed on a later statement) leaves its predicate in the queue. The next supported
// Any(predicate) arrangement must not dequeue that stale predicate.
//
// HARD CONSTRAINT (R1): a mock call nested inside an arrangement must still be evaluated, and must
// not drop the outer arrangement's pending predicate.
// ---------------------------------------------------------------------------

public class ThirdFixMatcherQueueRegressionTests : MockingTestBase
{
  [Fact]
  public void Local_matcher_passed_to_a_plain_call_does_not_leak_into_the_next_arrangement()
  {
    var g = A<IGreeter>();

    var m = Any<string>(s => s == "x");
    _ = g.Greet(m);

    g.Greet(Any<string>(s => s == "y")).Returns("yok");

    // The stale predicate (s == "x") must not bind; only the fresh one (s == "y") may match.
    Assert(() => g.Greet("x") == null);
    Assert(() => g.Greet("y") == "yok");
  }

  [Fact]
  public void Local_matcher_leak_does_not_corrupt_a_later_nested_standalone_arrangement()
  {
    var joiner = A<IJoiner>();
    var other = A<ICounter>(x => x.Count().Returns(7));

    var stale = Any<string>(s => s == "stale");
    _ = joiner.Greet(stale);

    // R1 shape: the nested other.Count() must still evaluate, and the outer Any(s == "y") must bind.
    joiner.Join(Any<string>(s => s == "y"), other.Count()).Returns("ok");

    Assert(() => joiner.Join("y", 7) == "ok");
    Assert(() => joiner.Join("y", 0) == null);
  }

  [Fact]
  public void Local_matcher_does_not_leak_into_an_arrange_lambda()
  {
    var stale = Any<string>(s => s == "stale");
    var g = A<IGreeter>();
    _ = g.Greet(stale);

    var repo = A<IGreeter>(x => x.Greet(Any<string>(s => s == "fresh")).Returns("fresh"));

    Assert(() => repo.Greet("fresh") == "fresh");
    Assert(() => repo.Greet("stale") == null);
  }
}
