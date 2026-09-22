using System;
using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

// ---------------------------------------------------------------------------
// Regression tests for the second review of PR #12 (re-review of e03ddb0).
// These cover the R1 regression and the remaining M3/M4 gaps.
// ---------------------------------------------------------------------------

public interface IJoiner
{
  string Join(string a, int b);
  string Greet(string name);
}

public interface ICounter
{
  int Count();
}

public interface IIndexedStore
{
  string this[int i] { get; }
}

/// <summary>
/// R1 — a mock call nested inside a standalone arrangement must run normally; only the call that
/// is the direct receiver of the arrange verb may be intercepted/swallowed.
/// </summary>
public class NestedStandaloneArrangeRegressionTests : MockingTestBase
{
  [Fact]
  public void Nested_mock_call_inside_a_standalone_matcher_arrangement_is_evaluated()
  {
    var joiner = A<IJoiner>();
    var other = A<ICounter>(x => x.Count().Returns(7));

    joiner.Join(Any<string>(s => s == "y"), other.Count()).Returns("ok");

    Assert(() => joiner.Join("y", 7) == "ok");
    Assert(() => joiner.Join("y", 0) == null);
  }

  [Fact]
  public void Nested_mock_call_in_a_standalone_Returns_value_is_evaluated()
  {
    var joiner = A<IJoiner>();
    var other = A<ICounter>(x => x.Count().Returns(42));

    joiner.Greet("v").Returns("v" + other.Count());

    Assert(() => other.Count() == 42);
    Assert(() => joiner.Greet("v") == "v42");
  }
}

/// <summary>
/// M4 — the strict-arrange exemption must be scoped to the one mock the When probe is arranging.
/// </summary>
public class StrictArrangeProbeScopeRegressionTests : MockingTestBase
{
  [Fact]
  public void Another_strict_mock_called_inside_a_When_probe_still_throws()
  {
    var arranged = A<ICounter>(MockMode.Strict);
    var other = A<ICounter>(MockMode.Strict);

    var ex = Throws<StrictMockException>(() => When(() =>
    {
      arranged.Count();
      other.Count();
    }));

    Assert(() => ex.Message.Contains("no matching arrangement"));
  }

  [Fact]
  public void The_mock_being_probed_is_still_exempt_from_the_strict_check()
  {
    var greeter = A<IGreeter>(MockMode.Strict);

    When(() => greeter.Greet("x")).Throws(new InvalidOperationException("boom"));

    Throws<InvalidOperationException>(() => greeter.Greet("x"));
  }
}

/// <summary>
/// M4 — properties/indexers cannot be intercepted, so standalone arrange on a strict mock is
/// rejected with an explicit, actionable message instead of a bare strict violation.
/// </summary>
public class StrictPropertyArrangeRegressionTests : MockingTestBase
{
  [Fact]
  public void Property_can_be_arranged_on_a_strict_mock_inside_the_arrange_lambda()
  {
    var svc = A<INamedService>(m => m.Name.Returns("Alice"), MockMode.Strict);

    Assert(() => svc.Name == "Alice");
  }

  [Fact]
  public void Standalone_property_arrange_on_a_strict_mock_gives_an_explicit_error()
  {
    var svc = A<INamedService>(MockMode.Strict);

    var ex = Throws<StrictMockException>(() => svc.Name.Returns("Alice"));

    Assert(() => ex.Message.Contains("Standalone arrangement of a property or indexer"));
    Assert(() => ex.Message.Contains("A<T>(m => m.Prop.Returns(value))"));
  }

  [Fact]
  public void Indexer_can_be_arranged_on_a_strict_mock_inside_the_arrange_lambda()
  {
    var store = A<IIndexedStore>(m => m[0].Returns("zero"), MockMode.Strict);

    Assert(() => store[0] == "zero");
  }

  [Fact]
  public void Standalone_indexer_arrange_on_a_strict_mock_gives_an_explicit_error()
  {
    var store = A<IIndexedStore>(MockMode.Strict);

    var ex = Throws<StrictMockException>(() => store[0].Returns("zero"));

    Assert(() => ex.Message.Contains("Standalone arrangement of a property or indexer"));
  }
}
