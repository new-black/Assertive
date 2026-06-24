using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Assertive.Mocking;
using Xunit;
using static Assertive.DSL;
using static Assertive.Mocking.Mock;

public interface IZeroArgVoidService
{
  void Reset();
}

public interface ITwoArgVoidService
{
  void Execute(int a, int b);
}

public interface IAsyncMultiArgService
{
  Task<string> FetchAsync(int id, string name);
}

public interface IValueTaskService
{
  ValueTask<int> GetAsync();
  ValueTask<int> ComputeAsync(int a, int b);
}

public class CoverageGapTests : MockingTestBase
{
  // ---------------------------------------------------------------------------
  // Higher-arity ValueArrange method-group arrangements
  // ---------------------------------------------------------------------------

  [Fact]
  public void Any_arity4_Returns_computed()
  {
    var calc = A<ICalculator>();

    Any(calc.Multiply).Returns((a, b, c, d) => a * b * c * d);

    Assert(() => calc.Multiply(1, 2, 3, 4) == 24);
  }

  [Fact]
  public void Any_arity4_ReturnsSequentially()
  {
    var calc = A<ICalculator>();

    Any(calc.Multiply).ReturnsSequentially(1, 2, 3);

    Assert(() => calc.Multiply(0, 0, 0, 0) == 1);
    Assert(() => calc.Multiply(0, 0, 0, 0) == 2);
    Assert(() => calc.Multiply(0, 0, 0, 0) == 3);
    Assert(() => calc.Multiply(0, 0, 0, 0) == 3); // last repeats
  }

  [Fact]
  public void Any_arity4_Throws()
  {
    var calc = A<ICalculator>();

    Any(calc.Multiply).Throws(new InvalidOperationException("boom"));

    var ex = Record.Exception(() => calc.Multiply(1, 2, 3, 4));

    Assert(() => ex is InvalidOperationException);
  }

  // ---------------------------------------------------------------------------
  // VoidArrange method-group arrangements across arities
  // ---------------------------------------------------------------------------

  [Fact]
  public void Any_void_arity0_Does_runs_callback()
  {
    var reset = false;
    var svc = A<IZeroArgVoidService>();

    Mock.Any(svc.Reset).Does(() => reset = true);

    svc.Reset();

    Assert(() => reset);
  }

  [Fact]
  public void Any_void_arity0_Throws()
  {
    var svc = A<IZeroArgVoidService>();

    Mock.Any(svc.Reset).Throws(new InvalidOperationException("no reset"));

    var ex = Record.Exception(() => svc.Reset());

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Any_void_arity0_Throws_generic()
  {
    var svc = A<IZeroArgVoidService>();

    Mock.Any(svc.Reset).Throws<InvalidOperationException>();

    var ex = Record.Exception(() => svc.Reset());

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Any_void_arity2_Does_runs_callback()
  {
    var seen = new List<string>();
    var svc = A<ITwoArgVoidService>();

    Any(svc.Execute).Does((a, b) => seen.Add($"{a}:{b}"));

    svc.Execute(1, 2);

    Assert(() => seen.Count == 1);
    Assert(() => seen[0] == "1:2");
  }

  [Fact]
  public void Any_void_arity2_Throws()
  {
    var svc = A<ITwoArgVoidService>();

    Any(svc.Execute).Throws(new InvalidOperationException("nope"));

    var ex = Record.Exception(() => svc.Execute(1, 2));

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Any_void_arity2_Throws_generic()
  {
    var svc = A<ITwoArgVoidService>();

    Any(svc.Execute).Throws<InvalidOperationException>();

    var ex = Record.Exception(() => svc.Execute(1, 2));

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Any_void_arity1_Throws_generic()
  {
    var greeter = A<IGreeter>();

    Any(greeter.Log).Throws<InvalidOperationException>();

    var ex = Record.Exception(() => greeter.Log("x"));

    Assert(() => ex is InvalidOperationException);
  }

  [Fact]
  public void Any_void_arity3_Throws_generic()
  {
    var calc = A<ICalculator>();

    Any(calc.Log).Throws<InvalidOperationException>();

    var ex = Record.Exception(() => calc.Log("A", "B", "C"));

    Assert(() => ex is InvalidOperationException);
  }

  // ---------------------------------------------------------------------------
  // WhenBuilder.Throws<TException>()
  // ---------------------------------------------------------------------------

  [Fact]
  public void When_Throws_generic_exception()
  {
    var greeter = A<IGreeter>(g =>
    {
      When(() => g.Log("x")).Throws<InvalidOperationException>();
    });

    var ex = Record.Exception(() => greeter.Log("x"));

    Assert(() => ex is InvalidOperationException);
  }

  // ---------------------------------------------------------------------------
  // Direct-call multi-arg computed Returns (ArrangeExtensions)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Direct_Returns_arity2_computed()
  {
    var greeter = A<IGreeter>();

    greeter.Complex(Any<byte>(), Any<string>()).Returns((byte a, string b) => $"{a}-{b}");

    Assert(() => greeter.Complex(1, "ok") == "1-ok");
  }

  [Fact]
  public void Direct_Returns_arity3_computed()
  {
    var calc = A<ICalculator>();

    calc.Add(Any<int>(), Any<int>(), Any<int>()).Returns<int, int, int, int>((a, b, c) => a + b + c);

    Assert(() => calc.Add(1, 2, 3) == 6);
  }

  [Fact]
  public void Direct_Returns_arity4_computed()
  {
    var calc = A<ICalculator>();

    calc.Multiply(Any<int>(), Any<int>(), Any<int>(), Any<int>()).Returns<int, int, int, int, int>((a, b, c, d) => a * b * c * d);

    Assert(() => calc.Multiply(1, 2, 3, 4) == 24);
  }

  // ---------------------------------------------------------------------------
  // Lazy async factories and ValueTask overloads
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task Lazy_Task_factory_Returns_invokes_factory()
  {
    var counter = 0;
    var greeter = A<IGreeter>();

    greeter.NameAsync().Returns(() => $"call {++counter}");

    var first = await greeter.NameAsync();
    var second = await greeter.NameAsync();

    Assert(() => first == "call 1");
    Assert(() => second == "call 2");
  }

  [Fact]
  public async Task ValueTask_direct_Returns_value()
  {
    var svc = A<IValueTaskService>();

    svc.GetAsync().Returns(42);

    var result = await svc.GetAsync();

    Assert(() => result == 42);
  }

  [Fact]
  public async Task ValueTask_direct_lazy_Returns_factory()
  {
    var counter = 0;
    var svc = A<IValueTaskService>();

    svc.GetAsync().Returns(() => ++counter);

    var first = await svc.GetAsync();
    var second = await svc.GetAsync();

    Assert(() => first == 1);
    Assert(() => second == 2);
  }

  [Fact]
  public async Task ValueTask_method_group_multi_arg_Returns_value()
  {
    var svc = A<IValueTaskService>();

    Any(svc.ComputeAsync).Returns(99);

    var result = await svc.ComputeAsync(1, 2);

    Assert(() => result == 99);
  }

  [Fact]
  public async Task ValueTask_method_group_multi_arg_Returns_computed()
  {
    var svc = A<IValueTaskService>();

    Any(svc.ComputeAsync).Returns((int a, int b) => new ValueTask<int>(a + b));

    var result = await svc.ComputeAsync(3, 4);

    Assert(() => result == 7);
  }

  // ---------------------------------------------------------------------------
  // AsyncArrangeExtensions multi-param Task overloads
  // ---------------------------------------------------------------------------

  [Fact]
  public async Task Async_method_group_multi_arg_Returns_value()
  {
    var svc = A<IAsyncMultiArgService>();

    Any(svc.FetchAsync).Returns("ok");

    var result = await svc.FetchAsync(7, "test");

    Assert(() => result == "ok");
  }

  [Fact]
  public async Task Async_method_group_multi_arg_Returns_computed()
  {
    var svc = A<IAsyncMultiArgService>();

    Any(svc.FetchAsync).Returns((int id, string name) => Task.FromResult($"{id}:{name}"));

    var result = await svc.FetchAsync(7, "test");

    Assert(() => result == "7:test");
  }

  // ---------------------------------------------------------------------------
  // Times.AtMostOnce
  // ---------------------------------------------------------------------------

  [Fact]
  public void Times_AtMostOnce_passes_when_not_exceeded()
  {
    var greeter = A<IGreeter>();

    greeter.Greet("Bob");

    Received(() => greeter.Greet("Bob"), Times.AtMostOnce);
  }

  [Fact]
  public void Times_AtMostOnce_fails_when_exceeded()
  {
    var greeter = A<IGreeter>();

    greeter.Greet("Bob");
    greeter.Greet("Bob");

    ShouldFail(() => Received(() => greeter.Greet("Bob"), Times.AtMostOnce));
  }
}
