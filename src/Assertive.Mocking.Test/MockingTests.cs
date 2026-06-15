using System;
using System.Linq;
using System.Threading.Tasks;
using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

// ---------------------------------------------------------------------------
// Shared test interfaces and classes (mirrored from Demo/Program.cs)
// ---------------------------------------------------------------------------

public interface IGreeter
{
  string Greet(string name);
  void Log(string message);
  string Complex(byte a, string b);
  IGizmo GetGizmo(string id);
  Task DoWorkAsync();
  Task<string> NameAsync();
  Task<IGizmo> GetGizmoAsync(string id);
}

public interface IGizmo
{
  string Serial();
  IWidget Widget();
}

public interface IWidget
{
  int Size();
}

public abstract class Repository
{
  public virtual string Load(int id) => $"REAL-{id}";
  public abstract int Count();
  public string Name => "the-repo";
}

public class Service
{
  private readonly string _env;
  public Service(string env) => _env = env;
  public virtual string Describe() => $"service in {_env}";
}

// ---------------------------------------------------------------------------
// Scenario 1 & 2: basic Mock.Of + Setup + Received
// ---------------------------------------------------------------------------

public class ArrangeTests : MockingTestBase
{
  [Fact]
  public void Setup_returns_configured_value()
  {
    var greeter = A<IGreeter>();
    Setup(greeter, g => g.Greet("Bob").Returns("Hello, Bob!"));

    var result = greeter.Greet("Bob");

    Assert(() => result == "Hello, Bob!");
  }

  [Fact]
  public void Received_passes_when_call_was_made()
  {
    var greeter = A<IGreeter>();
    Setup(greeter, g => g.Greet("Bob").Returns("Hello, Bob!"));

    greeter.Greet("Bob");

    // Should not throw
    Received(greeter, g => g.Greet("Bob"));
  }

  [Fact]
  public void Received_fails_when_called_with_wrong_arguments()
  {
    var greeter = A<IGreeter>();

    greeter.Greet("Adam the Magnificent");

    // Received for a different argument — should throw an Assertive failure
    ShouldFail(() => Received(greeter, g => g.Greet("Bob the Builder")));
  }

  [Fact]
  public void Received_fails_when_method_was_never_called()
  {
    var greeter = A<IGreeter>();

    greeter.Log("starting up");
    greeter.Log("shutting down");

    // Greet was never called
    ShouldFail(() => Received(greeter, g => g.Greet("anyone")));
  }

  [Fact]
  public void Unarranged_method_returns_default_on_loose_mock()
  {
    var greeter = A<IGreeter>();

    var result = greeter.Greet("someone");

    Assert(() => result == null);
  }
}

// ---------------------------------------------------------------------------
// Scenario 4: A<T>(arrange) DSL
// ---------------------------------------------------------------------------

public class ADslTests
{
  [Fact]
  public void A_executes_arrange_lambda_and_configures_returns()
  {
    var name = "Bob";
    var greeter = A<IGreeter>(g =>
    {
      Any(g.Greet).Returns("Hello " + name);
      g.Greet("Alice").Returns("Hi Alice");
    });

    Assert(() => greeter.Greet("Bob") == "Hello Bob");
    Assert(() => greeter.Greet("Alice") == "Hi Alice");
  }

  [Fact]
  public void A_more_specific_arrangement_wins_over_any()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.Greet).Returns("fallback");
      g.Greet("Alice").Returns("Hi Alice");
    });

    // Last-registered wins, so specific "Alice" arrangement overrides the Any
    Assert(() => greeter.Greet("Alice") == "Hi Alice");
    Assert(() => greeter.Greet("someone-else") == "fallback");
  }

  [Fact]
  public void A_Received_verifies_calls_made_after_arrange()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.Greet).Returns("Hello");
    });

    greeter.Greet("Bob");

    // Should not throw
    Received(greeter, g => g.Greet("Bob"));
  }

  [Fact]
  public void A_arrange_lambda_runs_side_effects()
  {
    var sideEffectRan = false;
    var greeter = A<IGreeter>(g =>
    {
      sideEffectRan = true;
      g.Greet("test").Returns("arranged");
    });

    Assert(() => sideEffectRan);
    Assert(() => greeter.Greet("test") == "arranged");
  }
}

// ---------------------------------------------------------------------------
// Scenario 5: void methods — When(...).Throws / .Does
// ---------------------------------------------------------------------------

public class VoidMethodTests
{
  [Fact]
  public void When_Throws_throws_on_matching_void_call()
  {
    var greeter = A<IGreeter>(g =>
    {
      When(() => g.Log("boom")).Throws(new InvalidOperationException("logging is down"));
    });

    var ex = Throws<InvalidOperationException>(() => greeter.Log("boom"));
    Assert(() => ex.Message == "logging is down");
  }

  [Fact]
  public void When_Does_runs_callback_on_matching_void_call()
  {
    var logged = new List<string>();

    var greeter = A<IGreeter>(g =>
    {
      When(() => g.Log("audit")).Does(args => logged.Add((string)args[0]!));
    });

    greeter.Log("audit");

    Assert(() => logged.Count == 1);
    Assert(() => logged[0] == "audit");
  }

  [Fact]
  public void Void_unarranged_call_is_no_op_on_loose_mock()
  {
    var greeter = A<IGreeter>();

    // Should not throw
    greeter.Log("hello");
  }

  [Fact]
  public void When_Throws_does_not_throw_on_non_matching_void_call()
  {
    var greeter = A<IGreeter>(g =>
    {
      When(() => g.Log("boom")).Throws(new InvalidOperationException("logging is down"));
    });

    // "ignored" doesn't match "boom" — should be a no-op
    greeter.Log("ignored");
  }
}

// ---------------------------------------------------------------------------
// Scenario 6: Any(methodGroup) — match any args, dynamic return
// ---------------------------------------------------------------------------

public class MethodGroupTests
{
  [Fact]
  public void Any_with_fixed_return_matches_any_argument()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.Complex).Returns("complex result");
    });

    Assert(() => greeter.Complex(1, "anything") == "complex result");
    Assert(() => greeter.Complex(255, "other") == "complex result");
  }

  [Fact]
  public void Any_with_computed_return_uses_argument_value()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.Greet).Returns(n => "Hi " + n);
    });

    Assert(() => greeter.Greet("Bob") == "Hi Bob");
    Assert(() => greeter.Greet("Zoe") == "Hi Zoe");
  }

  [Fact]
  public void Any_on_void_method_with_Does_runs_callback()
  {
    var messages = new List<string>();

    var greeter = A<IGreeter>(g =>
    {
      Any(g.Log).Does(msg => messages.Add(msg));
    });

    greeter.Log("hello");
    greeter.Log("world");

    Assert(() => messages.SequenceEqual(new[] { "hello", "world" }));
  }
}

// ---------------------------------------------------------------------------
// Scenario 7: Matchers — default (any), Any<T>(), Any<T>(predicate), mixed
// ---------------------------------------------------------------------------

public class MatcherTests
{
  [Fact]
  public void It_Any_matches_any_argument()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet(Any<string>()).Returns("any-name");
    });

    Assert(() => greeter.Greet("Bob") == "any-name");
    Assert(() => greeter.Greet("Alice") == "any-name");
    Assert(() => greeter.Greet("Zoe") == "any-name");
  }

  [Fact]
  public void It_Any_predicate_matches_conditionally()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet(Any<string>()).Returns("any-name");
      g.Greet(Any<string>(n => n.StartsWith("A"))).Returns("starts-with-A");
    });

    Assert(() => greeter.Greet("Alice") == "starts-with-A");
    Assert(() => greeter.Greet("Bob") == "any-name");
  }

  [Fact]
  public void Default_literal_acts_as_any_matcher()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Complex(default, Any<string>()).Returns("complex-any");
      g.Complex(default, "ping").Returns("pong");
    });

    Assert(() => greeter.Complex(7, "ping") == "pong");
    Assert(() => greeter.Complex(9, "other") == "complex-any");
  }

  [Fact]
  public void Last_configured_arrangement_wins()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet(Any<string>()).Returns("general");
      g.Greet(Any<string>(n => n.StartsWith("A"))).Returns("starts-with-A");
    });

    // "Alice" starts with A -> most recently configured predicate wins
    Assert(() => greeter.Greet("Alice") == "starts-with-A");
    // "Bob" does not start with A -> falls back to general
    Assert(() => greeter.Greet("Bob") == "general");
  }
}

public interface IRepo
{
  string FindByIds(IEnumerable<long> ids);
  string FindAll(IEnumerable<long> ids);
}

public class CollectionMatcherTests
{
  [Fact]
  public void It_Contains_matches_when_collection_contains_item()
  {
    var repo = A<IRepo>(r => r.FindByIds(Contains(42L)).Returns("found"));
    Assert(() => repo.FindByIds(new long[] { 1L, 42L, 99L }) == "found");
  }

  [Fact]
  public void It_Contains_does_not_match_when_item_absent()
  {
    var repo = A<IRepo>(r => r.FindByIds(Contains(42L)).Returns("found"));
    Assert(() => repo.FindByIds(new long[] { 1L, 2L, 3L }) == null);
  }

  [Fact]
  public void It_Contains_works_in_Received_with_type_inference()
  {
    // Verifies that type inference works in both arrange and verify: no explicit IEnumerable<long>.
    var repo = A<IRepo>(r => r.FindByIds(Contains(42L)).Returns("ok"));
    repo.FindByIds(new long[] { 1L, 42L });
    Received(() => repo.FindByIds(Contains(42L)));
  }

  [Fact]
  public void It_IsEmpty_matches_empty_collection()
  {
    var repo = A<IRepo>(r => r.FindAll(IsEmpty<long>()).Returns("empty"));
    Assert(() => repo.FindAll(Array.Empty<long>()) == "empty");
  }

  [Fact]
  public void It_IsEmpty_does_not_match_non_empty_collection()
  {
    var repo = A<IRepo>(r => r.FindAll(IsEmpty<long>()).Returns("empty"));
    Assert(() => repo.FindAll(new long[] { 1L }) == null);
  }
}

// ---------------------------------------------------------------------------
// Scenario 8: Recursive auto-mock — interface returns mocked by default
// ---------------------------------------------------------------------------

public class AutoMockTests
{
  [Fact]
  public void Unarranged_interface_return_is_auto_mocked_not_null()
  {
    var greeter = A<IGreeter>();

    var gizmo = greeter.GetGizmo("g1");

    Assert(() => gizmo != null);
  }

  [Fact]
  public void Auto_mocked_interface_members_return_defaults_or_further_auto_mocks()
  {
    var greeter = A<IGreeter>();
    var gizmo = greeter.GetGizmo("g1");

    // Serial() returns default string (null for string)
    Assert(() => gizmo.Serial() == null);

    // Widget() returns another auto-mock (not null)
    var widget = gizmo.Widget();
    Assert(() => widget != null);

    // Size() returns default int (0)
    Assert(() => widget.Size() == 0);
  }

  [Fact]
  public void Auto_mock_is_memoized_same_args_same_instance()
  {
    var greeter = A<IGreeter>();

    var gizmo1 = greeter.GetGizmo("g1");
    var gizmo2 = greeter.GetGizmo("g1");

    Assert(() => ReferenceEquals(gizmo1, gizmo2));
  }

  [Fact]
  public void Auto_mock_different_args_different_instance()
  {
    var greeter = A<IGreeter>();

    var gizmo1 = greeter.GetGizmo("g1");
    var gizmo2 = greeter.GetGizmo("g2");

    Assert(() => !ReferenceEquals(gizmo1, gizmo2));
  }
}

// ---------------------------------------------------------------------------
// Scenario 9: Async auto-mock — Task / Task<T>
// ---------------------------------------------------------------------------

public class AsyncTests
{
  [Fact]
  public async Task DoWorkAsync_auto_returns_completed_task_not_null()
  {
    var greeter = A<IGreeter>();

    // Should not NRE or hang
    await greeter.DoWorkAsync();
  }

  [Fact]
  public async Task NameAsync_auto_returns_completed_task_with_default()
  {
    var greeter = A<IGreeter>();

    var name = await greeter.NameAsync();

    Assert(() => name == null);
  }

  [Fact]
  public async Task NameAsync_arranged_returns_configured_value()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.NameAsync).Returns("Bob");
    });

    var name = await greeter.NameAsync();

    Assert(() => name == "Bob");
  }

  [Fact]
  public async Task GetGizmoAsync_arranged_returns_configured_object()
  {
    var arranged = A<IGizmo>();
    var greeter = A<IGreeter>(g =>
    {
      g.GetGizmoAsync("g7").Returns(arranged);
    });

    var gizmo = await greeter.GetGizmoAsync("g7");

    Assert(() => ReferenceEquals(arranged, gizmo));
  }

  [Fact]
  public async Task GetGizmoAsync_unarranged_returns_auto_mocked_not_null()
  {
    var greeter = A<IGreeter>();

    var gizmo = await greeter.GetGizmoAsync("g1");

    Assert(() => gizmo != null);
  }

  [Fact]
  public async Task GetGizmoAsync_unarranged_auto_mock_two_levels_deep()
  {
    var greeter = A<IGreeter>();

    var gizmo = await greeter.GetGizmoAsync("g1");
    var size = gizmo.Widget().Size();

    Assert(() => size == 0);
  }

  [Fact]
  public void GetGizmoAsync_Throws_returns_faulted_task_not_synchronous_throw()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.GetGizmoAsync).Throws(new InvalidOperationException("backend down"));
    });

    // The call itself does NOT throw — it returns a faulted task
    var task = greeter.GetGizmoAsync("x");

    Assert(() => task != null);
    Assert(() => task.IsFaulted);
  }

  [Fact]
  public async Task GetGizmoAsync_Throws_surfaces_exception_on_await()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.GetGizmoAsync).Throws(new InvalidOperationException("backend down"));
    });

    var task = greeter.GetGizmoAsync("x");
    var ex = await Throws<InvalidOperationException>(() => task);

    Assert(() => ex.Message == "backend down");
  }
}

// ---------------------------------------------------------------------------
// Scenario 10: Strict mock — unarranged calls throw StrictMockException
// ---------------------------------------------------------------------------

public class StrictTests
{
  [Fact]
  public void Strict_mock_allows_arranged_call()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("Hello Bob");
    }, MockMode.Strict);

    Assert(() => greeter.Greet("Bob") == "Hello Bob");
  }

  [Fact]
  public void Strict_mock_throws_on_unarranged_call()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("Hello Bob");
    }, MockMode.Strict);

    Throws<StrictMockException>(() => greeter.Greet("Zoe"));
  }

  [Fact]
  public void Strict_mock_disables_auto_mock_for_interface_returns()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("Hello Bob");
    }, MockMode.Strict);

    Throws<StrictMockException>(() => greeter.GetGizmo("g1"));
  }

  [Fact]
  public void Strict_mock_throws_on_void_unarranged_call()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("Hello Bob");
    }, MockMode.Strict);

    Throws<StrictMockException>(() => greeter.Log("anything"));
  }

  [Fact]
  public void StrictMockException_message_names_method_and_args()
  {
    var greeter = A<IGreeter>(g => { }, MockMode.Strict);

    var ex = Throws<StrictMockException>(() => greeter.Greet("Zoe"));

    Assert(() => ex.Message.Contains("Greet"));
    Assert(() => ex.Message.Contains("Zoe"));
  }

  [Fact]
  public void Mock_Of_strict_mode_throws_on_any_call()
  {
    var greeter = A<IGreeter>(MockMode.Strict);

    Throws<StrictMockException>(() => greeter.Greet("anyone"));
  }
}

// ---------------------------------------------------------------------------
// Scenario 11: Class mocks — virtual/abstract override, CallBase, ctor forwarding
// ---------------------------------------------------------------------------

public class ClassMockTests
{
  [Fact]
  public void Abstract_method_arranged_returns_configured_value()
  {
    var repo = A<Repository>(r =>
    {
      r.Count().Returns(42);
    });

    Assert(() => repo.Count() == 42);
  }

  [Fact]
  public void Virtual_method_arranged_for_specific_arg_returns_configured_value()
  {
    var repo = A<Repository>(r =>
    {
      r.Load(99).Returns("MOCKED");
    });

    Assert(() => repo.Load(99) == "MOCKED");
  }

  [Fact]
  public void Virtual_method_unarranged_calls_real_base()
  {
    var repo = A<Repository>(r =>
    {
      r.Count().Returns(0);
      r.Load(99).Returns("MOCKED");
    });

    // Load(7) is not arranged — CallBase runs the real implementation
    Assert(() => repo.Load(7) == "REAL-7");
  }

  [Fact]
  public void Non_virtual_property_always_runs_real()
  {
    var repo = A<Repository>(r =>
    {
      r.Count().Returns(0);
    });

    // Name is non-virtual — always the real value
    Assert(() => repo.Name == "the-repo");
  }

  [Fact]
  public void Mock_Of_class_with_ctor_args_forwards_to_base()
  {
    var svc = A<Service>("prod");

    // The base ctor ran with "prod", so Describe() (virtual but unarranged) calls base
    Assert(() => svc.Describe() == "service in prod");
  }

  [Fact]
  public void Class_mock_virtual_method_arranged_overrides_base()
  {
    // Service only has a ctor that takes a string, so forward "test" as the ctor arg.
    // A<T> with ctor args isn't directly supported (A<T> uses empty ctor args), so
    // create via Mock.Of<T>(ctorArgs) and then arrange separately.
    var svc = A<Service>("test");
    Setup(svc, s => s.Describe().Returns("mocked-describe"));

    Assert(() => svc.Describe() == "mocked-describe");
  }
}

// ---------------------------------------------------------------------------
// New tests: arity 3+, abstract class properties, strict+args
// ---------------------------------------------------------------------------

public interface ICalculator
{
  int Add(int a, int b, int c);
  int Multiply(int a, int b, int c, int d);
  void Log(string level, string category, string message);
}

public abstract class AbstractWithProp
{
  public abstract string Title { get; set; }
  public virtual int Score { get; set; }
}

public class ArityTests
{
  [Fact]
  public void Any_arity3_Returns_fixed_value()
  {
    var calc = A<ICalculator>(c =>
    {
      Any(c.Add).Returns(99);
    });

    Assert(() => calc.Add(1, 2, 3) == 99);
    Assert(() => calc.Add(10, 20, 30) == 99);
  }

  [Fact]
  public void Any_arity3_Returns_computed()
  {
    var calc = A<ICalculator>(c =>
    {
      Any(c.Add).Returns((a, b, cc) => a + b + cc);
    });

    Assert(() => calc.Add(1, 2, 3) == 6);
    Assert(() => calc.Add(10, 20, 30) == 60);
  }

  [Fact]
  public void Any_arity4_Returns_fixed_value()
  {
    var calc = A<ICalculator>(c =>
    {
      Any(c.Multiply).Returns(1000);
    });

    Assert(() => calc.Multiply(1, 2, 3, 4) == 1000);
  }

  [Fact]
  public void Any_void_arity3_Does_runs_callback()
  {
    var logs = new List<string>();
    var calc = A<ICalculator>(c =>
    {
      Any(c.Log).Does((level, cat, msg) => logs.Add($"{level}:{cat}:{msg}"));
    });

    calc.Log("INFO", "test", "hello");

    Assert(() => logs.Count == 1);
    Assert(() => logs[0] == "INFO:test:hello");
  }
}

public class AbstractClassPropertyTests
{
  [Fact]
  public void Abstract_property_arranged_returns_configured_value()
  {
    var obj = A<AbstractWithProp>(o =>
    {
      o.Title.Returns("Mocked Title");
    });

    Assert(() => obj.Title == "Mocked Title");
  }

  [Fact]
  public void Abstract_property_unarranged_returns_default()
  {
    var obj = A<AbstractWithProp>();

    Assert(() => obj.Title == null);
  }

  [Fact]
  public void Virtual_property_unarranged_calls_base()
  {
    var obj = A<AbstractWithProp>();

    // Score is virtual with no backing in abstract class — base returns 0 (default int auto-prop)
    Assert(() => obj.Score == 0);
  }
}

public class StrictWithArgsTests
{
  [Fact]
  public void Mock_Of_with_mode_and_args_creates_strict_mock_with_ctor_args()
  {
    var svc = A<Service>(MockMode.Strict, "prod");

    // Describe is unarranged on a strict mock — should throw
    Throws<StrictMockException>(() => svc.Describe());
  }

  [Fact]
  public void Mock_Of_with_mode_loose_and_args_creates_loose_mock()
  {
    var svc = A<Service>(MockMode.Loose, "test");

    // Loose: unarranged virtual calls base
    Assert(() => svc.Describe() == "service in test");
  }
}

// ---------------------------------------------------------------------------
// Test interface for property tests
// ---------------------------------------------------------------------------

public interface INamedService
{
  string Name { get; set; }
  int Count { get; }
}

// ---------------------------------------------------------------------------
// Phase 2: Verification suite tests
// ---------------------------------------------------------------------------

public class VerificationTests : MockingTestBase
{
  [Fact]
  public void Times_Once_passes_when_called_exactly_once()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");

    // Should not throw
    Received(greeter, Times.Once, g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_Once_fails_when_called_twice()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");

    ShouldFail(() => Received(greeter, Times.Once, g => g.Greet("Bob")));
  }

  [Fact]
  public void Times_Never_passes_when_not_called()
  {
    var greeter = A<IGreeter>();

    // Should not throw
    Received(greeter, Times.Never, g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_Never_fails_when_called_once()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");

    ShouldFail(() => Received(greeter, Times.Never, g => g.Greet("Bob")));
  }

  [Fact]
  public void Times_AtLeastOnce_passes_when_called_multiple_times()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");
    greeter.Greet("Bob");

    // Should not throw
    Received(greeter, Times.AtLeastOnce, g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_AtLeastOnce_fails_when_not_called()
  {
    var greeter = A<IGreeter>();

    ShouldFail(() => Received(greeter, Times.AtLeastOnce, g => g.Greet("Bob")));
  }

  [Fact]
  public void Times_Exactly_passes_when_count_matches()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");

    // Should not throw
    Received(greeter, Times.Exactly(2), g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_Exactly_fails_when_count_differs()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");

    ShouldFail(() => Received(greeter, Times.Exactly(3), g => g.Greet("Bob")));
  }

  [Fact]
  public void Times_AtLeast_passes_when_count_at_threshold()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");

    Received(greeter, Times.AtLeast(2), g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_AtMost_passes_when_count_within_limit()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");

    Received(greeter, Times.AtMost(3), g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_AtMost_fails_when_count_exceeds_limit()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");
    greeter.Greet("Bob");
    greeter.Greet("Bob");

    ShouldFail(() => Received(greeter, Times.AtMost(3), g => g.Greet("Bob")));
  }

  [Fact]
  public void Times_Between_passes_when_count_in_range()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");

    Received(greeter, Times.Between(1, 3), g => g.Greet("Bob"));
  }

  [Fact]
  public void Times_Between_fails_when_count_outside_range()
  {
    var greeter = A<IGreeter>();

    ShouldFail(() => Received(greeter, Times.Between(2, 4), g => g.Greet("Bob")));
  }

  [Fact]
  public void DidNotReceive_passes_when_method_never_called()
  {
    var greeter = A<IGreeter>();
    greeter.Log("something");

    // Greet was never called
    DidNotReceive(greeter, g => g.Greet("Bob"));
  }

  [Fact]
  public void DidNotReceive_fails_when_method_was_called()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");

    ShouldFail(() => DidNotReceive(greeter, g => g.Greet("Bob")));
  }

  [Fact]
  public void VerifyNoOtherCalls_passes_when_all_calls_have_setup()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("Hello Bob");
    });

    greeter.Greet("Bob");

    // All calls match a setup — should not throw
    VerifyNoOtherCalls(greeter);
  }

  [Fact]
  public void VerifyNoOtherCalls_fails_when_unarranged_calls_exist()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("Hello Bob");
    });

    greeter.Greet("Bob");
    greeter.Log("unexpected log");  // no setup for this

    ShouldFail(() => VerifyNoOtherCalls(greeter));
  }

  [Fact]
  public void VerifyNoOtherCalls_passes_on_fresh_mock_with_no_calls()
  {
    var greeter = A<IGreeter>();

    // No calls at all — should not throw
    VerifyNoOtherCalls(greeter);
  }

  [Fact]
  public void ClearReceivedCalls_empties_the_call_log()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Log("test");

    ClearReceivedCalls(greeter);

    // After clearing, Received for any call should fail (no calls recorded)
    ShouldFail(() => Received(greeter, g => g.Greet("Bob")));
  }

  [Fact]
  public void Capture_records_argument_values_from_matching_calls()
  {
    var cap = new Capture<string>();
    var greeter = A<IGreeter>(g =>
    {
      g.Greet(Any(cap)).Returns("captured");
    });

    greeter.Greet("Alice");
    greeter.Greet("Bob");

    Assert(() => cap.Values.Count == 2);
    Assert(() => cap.Values[0] == "Alice");
    Assert(() => cap.Values[1] == "Bob");
    Assert(() => cap.Latest == "Bob");
  }

  [Fact]
  public void Capture_Latest_throws_when_no_values_recorded()
  {
    var cap = new Capture<string>();
    Throws<InvalidOperationException>(() => cap.Latest);
  }
}

// ---------------------------------------------------------------------------
// Mock Received / DidNotReceive (lambda form)

public class ReceivedDslTests : MockingTestBase
{
  [Fact]
  public void Received_passes_when_call_was_made()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    Received(() => greeter.Greet("Bob"));
  }

  [Fact]
  public void Received_throws_when_call_was_not_made()
  {
    var greeter = A<IGreeter>();
    ShouldFail(() => Received(() => greeter.Greet("Bob")));
  }

  [Fact]
  public void Received_with_Times_Once_passes_when_called_exactly_once()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    Received(() => greeter.Greet("Bob"), Times.Once);
  }

  [Fact]
  public void Received_with_Times_Once_throws_when_called_twice()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Greet("Bob");
    ShouldFail(() => Received(() => greeter.Greet("Bob"), Times.Once));
  }

  [Fact]
  public void DidNotReceive_passes_when_call_was_not_made()
  {
    var greeter = A<IGreeter>();
    DidNotReceive(() => greeter.Greet("Bob"));
  }

  [Fact]
  public void DidNotReceive_throws_when_call_was_made()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    ShouldFail(() => DidNotReceive(() => greeter.Greet("Bob")));
  }

  [Fact]
  public void Received_with_matcher_passes_when_any_matching_call_was_made()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Alice");
    Received(() => greeter.Greet(Any<string>()));
  }

  [Fact]
  public void Received_with_matcher_throws_when_no_call_was_made()
  {
    var greeter = A<IGreeter>();
    ShouldFail(() => Received(() => greeter.Greet(Any<string>())));
  }
}

// ---------------------------------------------------------------------------
// Phase 3: Interface property arrangement and verification
// ---------------------------------------------------------------------------

public class PropertyTests : MockingTestBase
{
  [Fact]
  public void Arranged_property_returns_configured_value()
  {
    var svc = A<INamedService>(s =>
    {
      s.Name.Returns("Alice");
    });

    Assert(() => svc.Name == "Alice");
  }

  [Fact]
  public void Unarranged_string_property_returns_default_null()
  {
    var svc = A<INamedService>();

    Assert(() => svc.Name == null);
  }

  [Fact]
  public void Unarranged_int_property_returns_default_zero()
  {
    var svc = A<INamedService>();

    Assert(() => svc.Count == 0);
  }

  [Fact]
  public void Received_passes_for_recorded_property_get()
  {
    var svc = A<INamedService>(s =>
    {
      s.Name.Returns("Bob");
    });

    _ = svc.Name;

    // Should not throw
    Received(svc, s => _ = s.Name);
  }

  [Fact]
  public void DidNotReceive_passes_when_property_not_read()
  {
    var svc = A<INamedService>();

    // Name was never read
    DidNotReceive(svc, s => _ = s.Name);
  }

  [Fact]
  public void DidNotReceive_fails_when_property_was_read()
  {
    var svc = A<INamedService>();
    _ = svc.Name;

    ShouldFail(() => DidNotReceive(svc, s => _ = s.Name));
  }

  [Fact]
  public void Property_set_is_recorded()
  {
    var svc = A<INamedService>();
    svc.Name = "Charlie";

    Received(svc, s => s.Name = "Charlie");
  }
}

// ---------------------------------------------------------------------------
// Phase 4: Behavior richness
// ---------------------------------------------------------------------------

public interface ISequenceSource
{
  string Next();
  IEnumerable<string> GetItems();
  List<int> GetNumbers();
  string[] GetNames();
}

public class BehaviorTests : MockingTestBase
{
  // 4a: Sequential returns
  [Fact]
  public void ReturnsMany_ConsumesOnePerCall()
  {
    var src = A<ISequenceSource>(s =>
    {
      Any(s.Next).ReturnsMany("a", "b", "c");
    });

    Assert(() => src.Next() == "a");
    Assert(() => src.Next() == "b");
    Assert(() => src.Next() == "c");
    // After exhaustion, last value repeats
    Assert(() => src.Next() == "c");
  }

  [Fact]
  public void ReturnsMany_via_ArrangeExtensions()
  {
    var src = A<ISequenceSource>(s =>
    {
      s.Next().ReturnsMany("x", "y");
    });

    Assert(() => src.Next() == "x");
    Assert(() => src.Next() == "y");
    Assert(() => src.Next() == "y");
  }

  // 4b: Empty collection defaults
  [Fact]
  public void EmptyCollection_IEnumerable_unarranged_returns_empty_not_null()
  {
    var src = A<ISequenceSource>();

    var items = src.GetItems();

    Assert(() => items != null);
    Assert(() => !items.Any());
  }

  [Fact]
  public void EmptyCollection_List_unarranged_returns_empty_not_null()
  {
    var src = A<ISequenceSource>();

    var numbers = src.GetNumbers();

    Assert(() => numbers != null);
    Assert(() => !numbers.Any());
  }

  [Fact]
  public void EmptyCollection_Array_unarranged_returns_empty_not_null()
  {
    var src = A<ISequenceSource>();

    var names = src.GetNames();

    Assert(() => names != null);
    Assert(() => !names.Any());
  }

  // 4c: Reset
  [Fact]
  public void Reset_ClearsCallsAndSetups()
  {
    var greeter = A<IGreeter>(g =>
    {
      Any(g.Greet).Returns("hello");
    });

    greeter.Greet("Bob");

    // Verify call was recorded before reset
    Received(greeter, g => g.Greet("Bob")); // would throw if call not recorded

    Reset(greeter);

    // Call log is cleared — Received should now fail
    ShouldFail(() => Received(greeter, g => g.Greet("Bob")));

    // Setup is cleared — unarranged on loose mock returns default
    var result = greeter.Greet("Bob");
    Assert(() => result == null);
  }

  [Fact]
  public void Reset_ThenRearrange_WorksCorrectly()
  {
    var greeter = A<IGreeter>();
    Setup(greeter, g => g.Greet("Bob").Returns("original"));

    Reset(greeter);

    Setup(greeter, g => g.Greet("Bob").Returns("new"));
    Assert(() => greeter.Greet("Bob") == "new");
  }

  // 4d: Richer matchers
  [Fact]
  public void IsNotNull_Matches_NonNull_Values()
  {
    var greeter = A<IGreeter>(g =>
    {
      g.Greet(IsNotNull<string>()).Returns("got something");
    });

    Assert(() => greeter.Greet("Alice") == "got something");
  }

  [Fact]
  public void IsIn_Matches_Listed_Values()
  {
    var greeter = A<IGreeter>(g =>
    {
      // General fallback first, then more specific — last-registered wins.
      g.Greet(Any<string>()).Returns("unknown");
      g.Greet(IsIn("Alice", "Bob")).Returns("known");
    });

    Assert(() => greeter.Greet("Alice") == "known");
    Assert(() => greeter.Greet("Bob") == "known");
    Assert(() => greeter.Greet("Zoe") == "unknown");
  }

  [Fact]
  public void IsInRange_Matches_Values_In_Range()
  {
    var calc = A<ICalculator>(c =>
    {
      c.Add(IsInRange(1, 5), Any<int>(), Any<int>()).Returns(99);
    });

    Assert(() => calc.Add(3, 0, 0) == 99);
    Assert(() => calc.Add(10, 0, 0) == 0); // outside range — default
  }
}

// ---------------------------------------------------------------------------
// Interfaces used by ArgumentEqualityTests / ConditionalSetupTests
// ---------------------------------------------------------------------------

public interface ICollectionService
{
  string Process(string[] items);
  int Sum(List<int> numbers);
}

// ---------------------------------------------------------------------------
// Task 1: Structural argument equality tests
// ---------------------------------------------------------------------------

public class ArgumentEqualityTests
{
  [Fact]
  public void Array_arguments_match_by_value()
  {
    var svc = A<ICollectionService>(s =>
    {
      s.Process(new[] { "a", "b" }).Returns("matched");
    });

    // Different instance, same contents — must match.
    var result = svc.Process(new[] { "a", "b" });
    Assert(() => result == "matched");
  }

  [Fact]
  public void List_arguments_match_by_value()
  {
    var svc = A<ICollectionService>(s =>
    {
      s.Sum(new List<int> { 1, 2, 3 }).Returns(42);
    });

    // Different instance, same contents — must match.
    var result = svc.Sum(new List<int> { 1, 2, 3 });
    Assert(() => result == 42);
  }

  [Fact]
  public void Array_arguments_do_not_match_different_contents()
  {
    var svc = A<ICollectionService>(s =>
    {
      s.Process(new[] { "a", "b" }).Returns("matched");
    });

    // Different contents — must NOT match (falls through to default null).
    var result = svc.Process(new[] { "a", "c" });
    Assert(() => result == null);
  }
}

// ---------------------------------------------------------------------------
// Task 2: Conditional setup tests
// ---------------------------------------------------------------------------

public class ConditionalSetupTests
{
  [Fact]
  public void Conditional_setup_only_fires_when_condition_true()
  {
    var flag = false;
    var greeter = A<IGreeter>(g =>
    {
      g.Greet("Bob").Returns("conditional", when: () => flag);
    });

    // Condition is false — setup is skipped, default null is returned.
    var resultFalse = greeter.Greet("Bob");
    Assert(() => resultFalse == null);

    flag = true;

    // Condition is true — setup fires.
    var resultTrue = greeter.Greet("Bob");
    Assert(() => resultTrue == "conditional");
  }

  [Fact]
  public void Conditional_setup_falls_through_to_unconditional_setup_when_false()
  {
    var useSpecial = false;
    var greeter = A<IGreeter>(g =>
    {
      // Unconditional fallback registered first.
      g.Greet("Bob").Returns("fallback");
      // Conditional override registered after (last-wins when condition holds).
      g.Greet("Bob").Returns("special", when: () => useSpecial);
    });

    // Condition false → last (conditional) setup skipped → second-to-last (unconditional) wins.
    var result1 = greeter.Greet("Bob");
    Assert(() => result1 == "fallback");

    useSpecial = true;

    // Condition true → conditional setup fires.
    var result2 = greeter.Greet("Bob");
    Assert(() => result2 == "special");
  }
}

// ---------------------------------------------------------------------------
// Indexer tests
// ---------------------------------------------------------------------------

public interface ICache
{
  string this[string key] { get; set; }
}

public class IndexerTests
{
  [Fact]
  public void Arranged_indexer_get_returns_configured_value()
  {
    var cache = A<ICache>(c =>
    {
      c["key"].Returns("value");
    });

    var result = cache["key"];

    Assert(() => result == "value");
  }

  [Fact]
  public void Unarranged_indexer_get_returns_default()
  {
    var cache = A<ICache>();

    var result = cache["missing"];

    Assert(() => result == null);
  }

  [Fact]
  public void Indexer_set_is_recorded()
  {
    var cache = A<ICache>();

    cache["key"] = "value";

    Received(cache, c => { c["key"] = "value"; });
  }
}

// ---------------------------------------------------------------------------
// Event tests
// ---------------------------------------------------------------------------

public interface IEventSource
{
  event EventHandler<string> MessageReceived;
  event Action<int> CountChanged;
}

public class EventTests
{
  [Fact]
  public void Subscribing_and_raising_fires_the_handler()
  {
    var source = A<IEventSource>();
    string? received = null;
    source.MessageReceived += (sender, msg) => received = msg;

    Raise(source, s => s.MessageReceived += null, null, "hello");

    Assert(() => received == "hello");
  }

  [Fact]
  public void Multiple_subscribers_all_receive_the_event()
  {
    var source = A<IEventSource>();
    var log = new List<string>();
    source.MessageReceived += (sender, msg) => log.Add("A:" + msg);
    source.MessageReceived += (sender, msg) => log.Add("B:" + msg);

    Raise(source, s => s.MessageReceived += null, null, "ping");

    Assert(() => log.Count == 2);
    Assert(() => log[0] == "A:ping");
    Assert(() => log[1] == "B:ping");
  }

  [Fact]
  public void Remove_correctly_unsubscribes()
  {
    var source = A<IEventSource>();
    var called = false;
    EventHandler<string> handler = (sender, msg) => called = true;
    source.MessageReceived += handler;
    source.MessageReceived -= handler;

    Raise(source, s => s.MessageReceived += null, null, "test");

    Assert(() => called == false);
  }

  [Fact]
  public void Raising_with_no_subscribers_does_not_throw()
  {
    var source = A<IEventSource>();

    // Should not throw
    Raise(source, s => s.MessageReceived += null, null, "ignored");
  }
}

// ---------------------------------------------------------------------------
// InOrder verification tests
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Wrap<T> tests
// ---------------------------------------------------------------------------

public interface ITransformer
{
  string Transform(string input);
  void Process(string data);
}

public class UpperCaseTransformer : ITransformer
{
  public string Transform(string input) => input.ToUpper();
  public void Process(string data) { }
}

public class RecordingTransformer : ITransformer
{
  private readonly List<string> _log;
  public RecordingTransformer(List<string> log) => _log = log;
  public string Transform(string input) => input.ToUpper();
  public void Process(string data) => _log.Add(data);
}

public class WrapTests
{
  [Fact]
  public void Wrap_calls_through_to_real_implementation_when_not_arranged()
  {
    var spy = Wrap<ITransformer>(new UpperCaseTransformer());
    Assert(() => spy.Transform("hello") == "HELLO");
  }

  [Fact]
  public void Wrap_records_all_calls_for_verification()
  {
    var spy = Wrap<ITransformer>(new UpperCaseTransformer());
    _ = spy.Transform("hello");
    Received(() => spy.Transform("hello"));
  }

  [Fact]
  public void Wrap_arranged_call_overrides_call_through()
  {
    var spy = Wrap<ITransformer>(new UpperCaseTransformer());
    Setup(spy, s => s.Transform("hello").Returns("OVERRIDDEN"));
    Assert(() => spy.Transform("hello") == "OVERRIDDEN");
  }

  [Fact]
  public void Wrap_non_arranged_overload_still_calls_through()
  {
    var spy = Wrap<ITransformer>(new UpperCaseTransformer());
    Setup(spy, s => s.Transform("hello").Returns("OVERRIDDEN"));
    // Different args → no match → calls through to real
    Assert(() => spy.Transform("world") == "WORLD");
  }

  [Fact]
  public void Wrap_void_method_calls_through_when_not_arranged()
  {
    var log = new List<string>();
    var spy = Wrap<ITransformer>(new RecordingTransformer(log));
    spy.Process("data");
    Assert(() => log.Count == 1 && log[0] == "data");
  }

  [Fact]
  public void Wrap_void_method_does_not_call_through_when_arranged()
  {
    var log = new List<string>();
    var spy = Wrap<ITransformer>(new RecordingTransformer(log));
    Setup(spy, s => When(() => s.Process("data")).Does(_ => { }));
    spy.Process("data");
    // Arrangement handled it — real implementation should NOT have run
    Assert(() => log.Count == 0);
  }

  [Fact]
  public void DidNotReceive_on_wrap_works()
  {
    var spy = Wrap<ITransformer>(new UpperCaseTransformer());
    DidNotReceive(() => spy.Transform("hello"));
  }
}

// ---------------------------------------------------------------------------
// Build<T> tests
// ---------------------------------------------------------------------------

public interface IPaymentGateway
{
  string Charge(decimal amount);
}

public interface IOrderLogger
{
  void Log(string message);
}

public class CheckoutService
{
  private readonly IPaymentGateway _gateway;
  private readonly IOrderLogger _logger;

  public CheckoutService(IPaymentGateway gateway, IOrderLogger logger)
  {
    _gateway = gateway;
    _logger = logger;
  }

  public string ProcessPayment(decimal amount)
  {
    _logger.Log($"Processing {amount}");
    return _gateway.Charge(amount);
  }
}

public class BuildTests
{
  [Fact]
  public void Build_with_no_args_auto_mocks_all_constructor_params()
  {
    var svc = Build<CheckoutService>();
    Assert(() => svc != null);
    // Both deps auto-mocked; return defaults
    Assert(() => svc.ProcessPayment(10m) == null);
  }

  [Fact]
  public void Build_with_one_arg_auto_mocks_the_rest()
  {
    var gateway = A<IPaymentGateway>(g => g.Charge(Any<decimal>()).Returns("ok"));
    var svc = Build<CheckoutService>(gateway);

    Assert(() => svc.ProcessPayment(99m) == "ok");
  }

  [Fact]
  public void Build_matches_args_by_type_not_position()
  {
    // Pass in reverse constructor order: logger first, gateway second
    var logger = A<IOrderLogger>();
    var gateway = A<IPaymentGateway>(g => g.Charge(Any<decimal>()).Returns("charged"));

    var svc = Build<CheckoutService>(logger, gateway);

    Assert(() => svc.ProcessPayment(50m) == "charged");
  }

  [Fact]
  public void Build_with_all_args_provided()
  {
    var gateway = A<IPaymentGateway>(g => g.Charge(Any<decimal>()).Returns("full-ok"));
    var logger = A<IOrderLogger>();

    var svc = Build<CheckoutService>(gateway, logger);

    Assert(() => svc.ProcessPayment(10m) == "full-ok");
  }
}

public class InOrderTests : MockingTestBase
{
  [Fact]
  public void InOrder_passes_when_calls_are_in_correct_order()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob");
    greeter.Log("done");

    // Should not throw: Greet was called before Log
    InOrder(greeter, g =>
    {
      g.Greet("Bob");
      g.Log("done");
    });
  }

  [Fact]
  public void InOrder_fails_when_calls_are_in_wrong_order()
  {
    var greeter = A<IGreeter>();
    // Log called first, then Greet
    greeter.Log("done");
    greeter.Greet("Bob");

    // InOrder expects Greet before Log — should throw
    ShouldFail(() => InOrder(greeter, g =>
    {
      g.Greet("Bob");
      g.Log("done");
    }));
  }

  [Fact]
  public void InOrder_passes_when_expected_calls_are_subset_with_interleaved_others()
  {
    var greeter = A<IGreeter>();
    greeter.Log("start");
    greeter.Greet("Bob");
    greeter.Log("middle");
    greeter.Log("done");

    // Only checking that Greet("Bob") appeared before Log("done"); other calls may be interleaved
    InOrder(greeter, g =>
    {
      g.Greet("Bob");
      g.Log("done");
    });
  }
}

// ---------------------------------------------------------------------------
// Complex argument formatting
// ---------------------------------------------------------------------------

public record OrderItem(int ProductId, string Name, int Quantity);

public class LegacyItem
{
  public int Id { get; set; }
  public string Label { get; set; } = "";
}

public interface IWarehouse
{
  bool Reserve(OrderItem item);
  bool Tag(LegacyItem item);
}

public class ComplexArgTests : MockingTestBase
{
  [Fact]
  public void Record_arg_shows_all_properties_in_received_calls()
  {
    var wh = A<IWarehouse>();
    wh.Reserve(new OrderItem(42, "Widget", 3));

    ShouldFail(() => Received(wh, w => w.Reserve(new OrderItem(99, "Gadget", 1))));
  }

  [Fact]
  public void Class_arg_shows_properties_in_received_calls()
  {
    var wh = A<IWarehouse>();
    wh.Tag(new LegacyItem { Id = 1, Label = "foo" });
    ShouldFail(() => Received(wh, w => w.Tag(new LegacyItem { Id = 2 })));
  }
}

// ---------------------------------------------------------------------------
// Standalone arrange: var mock = A<T>(); mock.Method(args).Returns(v);
// ---------------------------------------------------------------------------

public class StandaloneArrangeTests : MockingTestBase
{
  [Fact]
  public void Returns_works_without_arrange_lambda()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob").Returns("Hello, Bob!");

    Assert(() => greeter.Greet("Bob") == "Hello, Bob!");
  }

  [Fact]
  public void Standalone_arrange_is_exact_arg_match()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob").Returns("Hello, Bob!");

    Assert(() => greeter.Greet("Bob") == "Hello, Bob!");
    Assert(() => greeter.Greet("Alice") == null);
  }

  [Fact]
  public void Multiple_standalone_arrangements_each_attach_correctly()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bob").Returns("Hello, Bob!");
    greeter.Greet("Alice").Returns("Hello, Alice!");

    Assert(() => greeter.Greet("Bob") == "Hello, Bob!");
    Assert(() => greeter.Greet("Alice") == "Hello, Alice!");
  }

  [Fact]
  public void Throws_works_without_arrange_lambda()
  {
    var greeter = A<IGreeter>();
    greeter.Greet("Bad").Throws(new InvalidOperationException("nope"));

    var threw = false;
    try { greeter.Greet("Bad"); }
    catch (InvalidOperationException) { threw = true; }
    Assert(() => threw);
  }

  [Fact]
  public void Does_works_without_arrange_lambda()
  {
    var greeter = A<IGreeter>();
    string? captured = null;
    greeter.Greet("Bob").Does(args =>
    {
      captured = (string?)args[0];
      return "Hi!";
    });

    var result = greeter.Greet("Bob");

    Assert(() => result == "Hi!");
    Assert(() => captured == "Bob");
  }

  [Fact]
  public void Standalone_and_lambda_arrange_can_be_mixed()
  {
    var greeter = A<IGreeter>(g => g.Greet("Bob").Returns("Lambda Bob"));
    greeter.Greet("Alice").Returns("Standalone Alice");

    Assert(() => greeter.Greet("Bob") == "Lambda Bob");
    Assert(() => greeter.Greet("Alice") == "Standalone Alice");
  }

  [Fact]
  public void Standalone_arrange_with_any_matcher()
  {
    var greeter = A<IGreeter>();
    greeter.Greet(Any<string>()).Returns("Hello, anyone!");

    Assert(() => greeter.Greet("Bob") == "Hello, anyone!");
    Assert(() => greeter.Greet("Alice") == "Hello, anyone!");
  }
}
