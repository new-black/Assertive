namespace Assertive.Mocking.Test.Aot
{
  using System;
  using Assertive.Mocking;
  using static Assertive.Mocking.Mock;

  /// <summary>
  /// Native AOT smoke test: a plain console app (test frameworks don't run under AOT)
  /// published with PublishAot=true. Each check exercises a reflective path of
  /// Assertive.Mocking (interface mock creation, arrangement, verification, class mock,
  /// auto-mock) to prove the library is trimming- and AOT-safe.
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

    // ── Check 3: Mock.Received passes when call was made ────────────────────

    private static void InterfaceMock_Received_passes_for_matching_call()
    {
      var svc = A<IService>();
      Mock.Setup(svc, s => s.GetValue().Returns(1));

      svc.GetValue();

      // Should not throw
      Mock.Received(svc, s => s.GetValue());
    }

    // ── Check 4: Mock.Received throws when method was not called ────────────

    private static void InterfaceMock_Received_fails_when_not_called()
    {
      var svc = A<IService>();

      Exception? ex = null;
      try
      {
        Mock.Received(svc, s => s.GetValue());
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

  public class ConcreteService
  {
    private readonly string _env;
    public ConcreteService(string env) => _env = env;
    public virtual string Describe() => $"service:{_env}";
  }
}
