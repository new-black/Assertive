using System;
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

