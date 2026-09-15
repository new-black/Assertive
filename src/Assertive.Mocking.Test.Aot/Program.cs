using System;
using System.Threading.Tasks;
using Assertive.Mocking;
using static Assertive.Mocking.Mock;

namespace Assertive.Mocking.Test.Aot
{
  /// <summary>
  /// Native AOT smoke test: a plain console app (test frameworks don't run under AOT)
  /// published with PublishAot=true. Each check exercises a trimming-sensitive path of
  /// Assertive.Mocking — interface/class mock creation, arrangement, verification, auto-mocks,
  /// Wrap spies, Build factories, async returns, matchers/method groups and Capture — to prove
  /// the library is trimming- and AOT-safe.
  ///
  /// Usings are intentionally at file scope: the generated MockArrange.Any overloads are imported
  /// via a global using, and a namespace-scoped <c>using static</c> suppresses them during method
  /// group overload resolution (Any(mock.Method)).
  ///
  /// Exit code is the number of failed checks (0 = all passed).
  /// </summary>
  public static class Program
  {
    private static int _failed;

    public static int Main(string[] args)
    {
      Console.WriteLine("Assertive.Mocking AOT smoke test");
      Console.WriteLine();

      Check(nameof(InterfaceMock_basic_arrange_and_call), InterfaceMock_basic_arrange_and_call);
      Check(nameof(InterfaceMock_A_dsl_returns_configured_value), InterfaceMock_A_dsl_returns_configured_value);
      Check(nameof(InterfaceMock_Received_passes_for_matching_call), InterfaceMock_Received_passes_for_matching_call);
      Check(nameof(InterfaceMock_Received_fails_when_not_called), InterfaceMock_Received_fails_when_not_called);
      Check(nameof(ClassMock_virtual_method_arranged), ClassMock_virtual_method_arranged);
      Check(nameof(ClassMock_unarranged_virtual_calls_base), ClassMock_unarranged_virtual_calls_base);
      Check(nameof(AutoMock_interface_return_is_not_null), AutoMock_interface_return_is_not_null);
      Check(nameof(Wrap_spy_delegates_then_overrides), Wrap_spy_delegates_then_overrides);
      Check(nameof(Build_constructs_with_auto_mocked_dependency), Build_constructs_with_auto_mocked_dependency);
      Check(nameof(Async_Task_and_ValueTask_returns), Async_Task_and_ValueTask_returns);
      Check(nameof(Matchers_and_method_group_arrange), Matchers_and_method_group_arrange);
      Check(nameof(Times_and_capture_verification), Times_and_capture_verification);

      Console.WriteLine();
      Console.WriteLine(_failed == 0 ? "All checks passed." : $"{_failed} check(s) FAILED.");

      return _failed;
    }

    // ── Check 1: basic Mock.Setup + call ────────────────────────────────────

    private static void InterfaceMock_basic_arrange_and_call()
    {
      var svc = A<IService>();
      Mock.Setup(svc, s => s.GetValue().Returns(42));

      var result = svc.GetValue();

      Expect(result == 42, $"expected 42 but got {result}");
    }

    // ── Check 2: A<T> DSL with Returns ──────────────────────────────────────

    private static void InterfaceMock_A_dsl_returns_configured_value()
    {
      var svc = A<IService>(s =>
      {
        s.GetValue().Returns(99);
      });

      var result = svc.GetValue();

      Expect(result == 99, $"expected 99 but got {result}");
    }

    // ── Check 3: Received passes when call was made ────────────────────────

    private static void InterfaceMock_Received_passes_for_matching_call()
    {
      var svc = A<IService>();
      Mock.Setup(svc, s => s.GetValue().Returns(1));

      svc.GetValue();

      // Should not throw
      Received(() => svc.GetValue());
    }

    // ── Check 4: Received throws when method was not called ─────────────────

    private static void InterfaceMock_Received_fails_when_not_called()
    {
      var svc = A<IService>();

      Exception? ex = null;
      try
      {
        Received(() => svc.GetValue());
      }
      catch (Exception caught)
      {
        ex = caught;
      }

      Expect(ex != null, "Received should throw when method was never called");
    }

    // ── Check 5: class mock — virtual method arranged ───────────────────────

    private static void ClassMock_virtual_method_arranged()
    {
      var svc = A<ConcreteService>("aot-env");
      Mock.Setup(svc, s => s.Describe().Returns("mocked-describe"));

      var result = svc.Describe();

      Expect(result == "mocked-describe", $"expected 'mocked-describe' but got '{result}'");
    }

    // ── Check 6: class mock — unarranged virtual calls base ─────────────────

    private static void ClassMock_unarranged_virtual_calls_base()
    {
      var svc = A<ConcreteService>("aot-env");

      // Describe is virtual and unarranged — the real base should run.
      var result = svc.Describe();

      Expect(result == "service:aot-env", $"expected 'service:aot-env' but got '{result}'");
    }

    // ── Check 7: auto-mock for interface-returning method is not null ────────

    private static void AutoMock_interface_return_is_not_null()
    {
      var svc = A<IService>();

      var dep = svc.GetDependency();

      Expect(dep != null, "unarranged interface-returning method should return an auto-mock, not null");
    }

    // ── Check 8: Wrap<T> passes through then honours arrangements ───────────

    private static void Wrap_spy_delegates_then_overrides()
    {
      var spy = Wrap<ITransformer>(new UpperTransformer());

      var passthrough = spy.Transform("abc");
      Expect(passthrough == "ABC", $"expected 'ABC' but got '{passthrough}'");

      Setup(spy, s => s.Transform("abc").Returns("OVERRIDDEN"));

      var overridden = spy.Transform("abc");
      var stillPassthrough = spy.Transform("zzz");

      Expect(overridden == "OVERRIDDEN", $"expected 'OVERRIDDEN' but got '{overridden}'");
      Expect(stillPassthrough == "ZZZ", $"expected 'ZZZ' but got '{stillPassthrough}'");
      Received(() => spy.Transform("zzz"));
    }

    // ── Check 9: Build<T> constructs with auto-mocked dependencies ──────────

    private static void Build_constructs_with_auto_mocked_dependency()
    {
      var svc = Build<ConsumerService>();

      var dependency = svc.Dependency;
      if (dependency is null)
      {
        throw new Exception("Build should auto-mock the constructor dependency");
      }

      Expect(dependency.Tag() == null, "auto-mocked dependency should return its default");
    }

    // ── Check 10: Task<T>/ValueTask<T> bare-value Returns ───────────────────

    private static void Async_Task_and_ValueTask_returns()
    {
      var svc = A<IAsyncService>(s =>
      {
        s.GetNameAsync().Returns("bob");
        s.GetCountAsync().Returns(7);
      });

      var name = svc.GetNameAsync().GetAwaiter().GetResult();
      var count = svc.GetCountAsync().GetAwaiter().GetResult();

      Expect(name == "bob", $"expected 'bob' but got '{name}'");
      Expect(count == 7, $"expected 7 but got {count}");
    }

    // ── Check 11: matchers + method-group arrangement (delegate reflection) ─

    private static void Matchers_and_method_group_arrange()
    {
      var repo = A<IRepo>();

      Any(repo.GetById).Returns(5);

      var broad = repo.GetById(3);
      Expect(broad == 5, $"expected 5 but got {broad}");

      var greeter = A<IGreeter>();
      greeter.Greet(Any<string>(name => name.StartsWith("A"))).Returns("hi A");

      var matched = greeter.Greet("Alice");
      var unmatched = greeter.Greet("Bob");

      Expect(matched == "hi A", $"expected 'hi A' but got '{matched}'");
      Expect(unmatched == null, $"expected null but got '{unmatched}'");
    }

    // ── Check 12: Times + Capture ───────────────────────────────────────────

    private static void Times_and_capture_verification()
    {
      var svc = A<IService>();
      svc.GetValue();
      svc.GetValue();

      Received(() => svc.GetValue(), Times.Exactly(2));

      var captured = new Capture<int>();
      var repo = A<IRepo>();
      repo.GetById(Any<int>(captured)).Returns(1);
      repo.GetById(42);

      Expect(captured.Latest == 42, $"expected captured 42 but got {captured.Latest}");
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static void Check(string name, Action check)
    {
      try
      {
        check();
        Console.WriteLine($"PASS {name}");
      }
      catch (Exception ex)
      {
        _failed++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine($"     {ex.Message.Replace("\n", "\n     ")}");
      }
    }

    private static void Expect(bool condition, string detail)
    {
      if (!condition)
      {
        throw new Exception(detail);
      }
    }
  }

  // ── Test types ──────────────────────────────────────────────────────────────

  public interface IDependency
  {
    string Tag();
  }

  public interface IService
  {
    int GetValue();
    IDependency GetDependency();
  }

  public interface IRepo
  {
    int GetById(int id);
  }

  public interface IGreeter
  {
    string Greet(string name);
  }

  public interface ITransformer
  {
    string Transform(string input);
  }

  public sealed class UpperTransformer : ITransformer
  {
    public string Transform(string input) => input.ToUpperInvariant();
  }

  public interface IAsyncService
  {
    Task<string> GetNameAsync();
    ValueTask<int> GetCountAsync();
  }

  public class ConsumerService
  {
    public IDependency Dependency { get; }

    public ConsumerService(IDependency dependency) => Dependency = dependency;
  }

  public class ConcreteService
  {
    private readonly string _env;
    public ConcreteService(string env) => _env = env;
    public virtual string Describe() => $"service:{_env}";
  }
}
