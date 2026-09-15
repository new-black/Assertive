using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

public class RuntimeGapTests
{
  [Fact]
  public void Class_mock_arrange_with_Any_matches_any_argument()
  {
    var calc = A<Calculator>(m => m.Add(Any<int>(), Any<int>()).Returns(42));

    Assert(() => calc.Add(1, 2) == 42);
    Assert(() => calc.Add(5, 7) == 42);
  }

  [Fact]
  public void DidNotReceive_with_Any_predicate_fails_when_matching_call_exists()
  {
    var repo = A<IRepository>();
    repo.GetById(5);

    var ex = Record.Exception(() => DidNotReceive(() => repo.GetById(Any<int>(id => id == 5))));

    Assert(() => ex != null);
  }

  [Fact]
  public void Received_with_collection_arguments_uses_structural_equality()
  {
    var processor = A<IProcessor>();
    processor.Sum(new[] { 1, 2, 3 });

    // Should match by value, not by reference.
    Received(() => processor.Sum(new[] { 1, 2, 3 }));
  }

  [Fact]
  public async Task Standalone_arrange_flows_across_await()
  {
    var repo = A<IRepository>();

    // Switch to a continuation; the AsyncLocal arrange/matcher state must flow across it.
    await Task.Delay(1);

    repo.GetById(Any<int>()).Returns(99);
    Assert(() => repo.GetById(123) == 99);
  }

  [Fact]
  public void Received_without_Mock_prefix_supports_Times()
  {
    var repo = A<IRepository>();

    repo.GetById(123);
    repo.GetById(123);
    repo.GetById(456);

    Received(() => repo.GetById(123), Times.Exactly(2));
    Received(() => repo.GetById(456), Times.AtLeastOnce);
    DidNotReceive(() => repo.GetById(999));

    // Lambda form with Times also works without the Mock prefix
    Received(() => repo.GetById(123), Times.AtLeastOnce);
  }

  [Fact]
  public void Standalone_When_applies_arrangement()
  {
    var greeter = A<IGreeter>();

    When(() => greeter.Log("boom")).Throws(new InvalidOperationException("offline"));

    var ex = Record.Exception(() => greeter.Log("boom"));

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Standalone_When_does_not_record_the_probe_call()
  {
    var greeter = A<IGreeter>();

    When(() => greeter.Log("audit")).Does(_ => { });

    // Capturing the arrangement must not show up as a received call.
    DidNotReceive(() => greeter.Log("audit"));
  }

  [Fact]
  public void Unconsumed_matchers_do_not_leak_into_a_later_arrange_scope()
  {
    // A matcher that is never consumed by an interceptor must not poison the next arrangement.
    _ = Any<int>(x => x > 5);

    var repo = A<IRepository>(m => m.GetById(Any<int>(x => x == 1)).Returns(42));

    Assert(() => repo.GetById(1) == 42);
    Assert(() => repo.GetById(7) == 0);
  }

  [Fact]
  public async Task Arrange_lambda_can_await_inside()
  {
    var repo = await A<IRepository>(async m =>
    {
      await Task.Delay(1);
      m.GetById(Any<int>()).Returns(77);
    });

    Assert(() => repo.GetById(42) == 77);
    Assert(() => repo.GetById(0) == 77);
  }

  [Fact]
  public void AutoMock_concurrent_calls_return_same_instance()
  {
    var factory = A<IFactory>();

    object?[] results = new object?[100];
    Parallel.For(0, 100, i => results[i] = factory.GetService("svc"));

    var distinct = results.Distinct().Count();
    Assert(() => distinct == 1);
  }

  [Fact]
  public void A_of_class_selects_constructor_matching_argument_types()
  {
    var service = A<ArgAwareService>("hello");

    Assert(() => service.Value == 5);
    Assert(() => service.Kind == "string");
  }

  [Fact]
  public void Build_selects_constructor_matching_argument_types()
  {
    var service = Build<Service>("hello");

    // String ctor should be chosen; Value set to string length.
    Assert(() => service.Value == 5);
    Assert(() => service.Kind == "string");
  }

  [Fact]
  public void Build_supports_eight_arguments()
  {
    var service = Build<OctoService>(1, 2, 3, 4, 5, 6, 7, 8);

    Assert(() => service.Sum == 36);
  }

  [Fact]
  public void Interface_generic_method_throws_NotSupportedException()
  {
    var generic = A<IGenericMethod>();

    var ex = Record.Exception(() => generic.Foo<int>());

    Assert(() => ex is NotSupportedException);
  }

  [Fact]
  public void Matcher_setup_compares_exact_args_structurally()
  {
    var svc = A<IMixedMatcherService>();

    svc.Mix(Any<int>(n => n > 0), new[] { 1, 2, 3 }).Returns(7);

    Assert(() => svc.Mix(5, new[] { 1, 2, 3 }) == 7);
  }

  [Fact]
  public void Received_lambda_with_multiple_mock_calls_throws()
  {
    var repo = A<IRepository>();
    repo.GetById(1);
    repo.GetById(2);

    var ex = Record.Exception(() => Received(() => { repo.GetById(1); repo.GetById(2); }));

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Build_passes_null_to_nullable_parameter()
  {
    var svc = Build<OptionalDependencyService>(null);

    Assert(() => svc.Repository == null);
  }

  [Fact]
  public void Reset_clears_auto_mock_cache()
  {
    var factory = A<IFactory>();

    var first = factory.GetService("svc");
    Reset(factory);
    var second = factory.GetService("svc");

    Assert(() => !ReferenceEquals(first, second));
  }

  [Fact]
  public void Strict_violation_is_not_recorded_in_the_call_log()
  {
    var repo = A<IRepository>(MockMode.Strict);

    var ex = Record.Exception(() => repo.GetById(1));

    Assert(() => ex is StrictMockException);

    var core = ((Assertive.Mocking.Runtime.IMockObject)(object)repo).Core;
    Assert(() => core.Calls.Count == 0);
  }

  [Fact]
  public void Matcher_like_call_on_real_object_inside_arrange_lambda_runs_real_method()
  {
    var list = new List<int> { 0, 1 };

    var greeter = A<IGreeter>(g =>
    {
      // `default` looks like a matcher; the interceptor must fall back to the real List.Contains.
      var found = list.Contains(default);
      g.Greet(Any<string>()).Returns(found ? "found" : "missing");
    });

    Assert(() => greeter.Greet("x") == "found");
  }

  [Fact]
  public void Matcher_on_optional_parameter_matches()
  {
    var svc = A<IOptionalParameterService>();

    svc.M(Any<int>(x => x > 0)).Returns(1);

    Assert(() => svc.M(5) == 1);       // y defaults to 7
    Assert(() => svc.M(5, 7) == 1);
    Assert(() => svc.M(5, 8) == 0);    // y differs
    Assert(() => svc.M(-1) == 0);      // predicate fails
  }

  [Fact]
  public void Matcher_on_expanded_params_elements_matches()
  {
    var svc = A<IParamsService>();

    svc.Sum(Any<int>(v => v > 0), 2).Returns(9);

    Assert(() => svc.Sum(1, 2) == 9);
    Assert(() => svc.Sum(-1, 2) == 0); // first element fails the predicate
    Assert(() => svc.Sum(1, 3) == 0);  // second element isn't 2
    Assert(() => svc.Sum(1) == 0);     // length must match
  }

  [Fact]
  public void Matcher_setup_does_not_cross_match_overloads()
  {
    var svc = A<IOverloadedService>();

    svc.M(Any<int>()).Returns("int");
    svc.M(Any<string>()).Returns("string");

    Assert(() => svc.M(5) == "int");
    Assert(() => svc.M("x") == "string");
  }

  [Fact]
  public void Method_group_Any_does_not_cross_match_arity_overloads()
  {
    var svc = A<IArityOverloadedService>();

    Any((Func<int, string>)svc.M).Returns("one");

    Assert(() => svc.M(1) == "one");
    Assert(() => svc.M(1, 2) == null);
  }

  [Fact]
  public void Wrap_arranged_property_setter_does_not_mutate_wrapped_object()
  {
    var target = new MutableService { Value = 1 };
    var spy = Wrap<IMutableService>(target);

    Setup(spy, s => When(() => s.Value = 42).Does(_ => { }));

    spy.Value = 42;

    Assert(() => target.Value == 1);
  }

  [Fact]
  public void Wrap_unarranged_property_setter_passes_through()
  {
    var target = new MutableService { Value = 1 };
    var spy = Wrap<IMutableService>(target);

    spy.Value = 5;

    Assert(() => target.Value == 5);
  }

  [Fact]
  public void Wrap_arranged_indexer_setter_does_not_mutate_wrapped_object()
  {
    var target = new IndexedService();
    target[1] = 10;
    var spy = Wrap<IIndexedService>(target);

    Setup(spy, s => When(() => s[1] = 99).Does(_ => { }));

    spy[1] = 99;

    Assert(() => target[1] == 10);
  }

  [Fact]
  public void Class_mock_arranged_setter_throws()
  {
    var mock = A<VirtualMutableService>();
    Setup(mock, m => When(() => m.Value = 42).Throws(new InvalidOperationException("nope")));

    var ex = Record.Exception(() => mock.Value = 42);

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Nested_arrange_scopes_do_not_clobber_the_outer_scope()
  {
    var innerRan = false;

    var repo = A<IRepository>(r =>
    {
      // Nested arrange scope on a different mock must not tear down the outer scope.
      _ = A<IProcessor>(p => p.Sum(Any<int[]>()).Returns(5));
      innerRan = true;
      r.GetById(Any<int>()).Returns(42);
    });

    Assert(() => innerRan);
    Assert(() => repo.GetById(7) == 42);
  }

  [Fact]
  public void Nested_arrange_scopes_on_the_same_mock_work()
  {
    var repo = A<IRepository>(r =>
    {
      r.GetById(Any<int>()).Returns(42);
      Setup(r, x => x.GetById(1).Returns(11));
    });

    Assert(() => repo.GetById(1) == 11);
    Assert(() => repo.GetById(9) == 42);
  }

  // ── test types ─────────────────────────────────────────────────────────────

  public class Calculator
  {
    public virtual int Add(int a, int b) => 0;
  }

  public interface IRepository
  {
    int GetById(int id);
  }

  public interface IProcessor
  {
    int Sum(int[] values);
  }

  public interface IService { }
  public interface IFactory { IService GetService(string name); }

  public interface IMixedMatcherService
  {
    int Mix(int n, int[] values);
  }

  public interface IOptionalParameterService
  {
    int M(int x, int y = 7);
  }

  public interface IParamsService
  {
    int Sum(params int[] values);
  }

  public interface IOverloadedService
  {
    string M(int x);
    string M(string x);
  }

  public interface IArityOverloadedService
  {
    string M(int x);
    string M(int x, int y);
  }

  public interface IMutableService
  {
    int Value { get; set; }
  }

  public class MutableService : IMutableService
  {
    public int Value { get; set; }
  }

  public interface IIndexedService
  {
    int this[int key] { get; set; }
  }

  public class IndexedService : IIndexedService
  {
    private readonly Dictionary<int, int> _values = new();

    public int this[int key]
    {
      get => _values[key];
      set => _values[key] = value;
    }
  }

  public class VirtualMutableService
  {
    public virtual int Value { get; set; }
  }

  public class OptionalDependencyService
  {
    public IRepository? Repository { get; }

    public OptionalDependencyService(IRepository? repository) => Repository = repository;
  }

  public class Service
  {
    public int Value { get; }
    public string Kind { get; }

    public Service(int value)
    {
      Value = value;
      Kind = "int";
    }

    public Service(string value)
    {
      Value = value.Length;
      Kind = "string";
    }
  }

  public class ArgAwareService
  {
    public int Value { get; }
    public string Kind { get; }

    public ArgAwareService(int value)
    {
      Value = value;
      Kind = "int";
    }

    public ArgAwareService(string value)
    {
      Value = value.Length;
      Kind = "string";
    }
  }

  public class OctoService
  {
    public int Sum { get; }

    public OctoService(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8)
    {
      Sum = a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8;
    }
  }

  public interface IGenericMethod
  {
    T Foo<T>();
  }
}

