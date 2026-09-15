using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Assertive.DSL;

public class GeneratorGapTests
{
  [Fact]
  public void Interface_with_in_parameter_compiles()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IInParam { void M(in int x); }
class Test { void M() { var m = A<IInParam>(); } }
";
    CompileGeneratedSource(source);
  }

  [Fact]
  public void Interface_generic_method_emits_NotSupported_stub()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IGenericMethod { T Foo<T>(); }
class Test { void M() { var m = A<IGenericMethod>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("T Foo<T>()"));
    Assert(() => output.Contains("NotSupportedException"));
  }

  [Fact]
  public void Abstract_class_with_generic_method_can_be_mocked()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public abstract class GenericAbstract { public abstract T Foo<T>(); }
class Test { void M() { var m = A<GenericAbstract>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("public override T Foo<T>()"));
    Assert(() => output.Contains("NotSupportedException"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Abstract_class_with_ref_return_can_be_mocked()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public abstract class RefReturnAbstract { public abstract ref int Get(); }
class Test { void M() { var m = A<RefReturnAbstract>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("public override ref int Get()"));
    Assert(() => output.Contains("throw new global::System.NotSupportedException"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Class_mock_overrides_protected_internal_virtual_member()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class ProtectedBase
{
  protected internal virtual int Compute(int x) => x;
  public int ComputePublic(int x) => Compute(x);
}
class Test { void M() { var m = A<ProtectedBase>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("protected internal override int Compute(int x)"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Wrap_class_emits_events()
  {
    var source = @"
using System;
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IWithEvent { event Action Something; }
class Test
{
  class Impl : IWithEvent { public event Action? Something; }
  void M() { var w = Wrap<IWithEvent>(new Impl()); }
}
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("class Wrap_"));
    Assert(() => output.Contains("event global::System.Action Something"));
    Assert(() => output.Contains("__wrapped.Something += value") || output.Contains("Something += value"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Class_with_same_arity_overloads_emits_both()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public abstract class Overloaded
{
  public abstract int M(int x);
  public abstract string M(string x);
}
class Test { void Run() { var m = A<Overloaded>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("override int M(int x)"));
    Assert(() => output.Contains("override string M(string x)"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Sealed_override_in_hierarchy_does_not_emit_illegal_override()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class SealedBase { public virtual int M() => 1; }
public class SealedDerived : SealedBase { public sealed override int M() => 2; }
class Test { void Run() { var m = A<SealedDerived>(); } }
";
    var output = RunGenerator(source);
    Assert(() => !output.Contains("override int M("));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Record_type_reports_error()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public record Person(string Name);
class Test { void Run() { var m = A<Person>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK001" && d.Severity == DiagnosticSeverity.Error));
    Assert(() => diagnostics.Any(d => d.Id == "MOCK001" && d.GetMessage().Contains("records cannot be mocked")));
  }

  [Fact]
  public void Class_without_accessible_constructor_reports_error()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class NoAccessibleCtor
{
  private NoAccessibleCtor() { }
  public virtual int M() => 1;
}
class Test { void Run() { var m = A<NoAccessibleCtor>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK001" && d.GetMessage().Contains("no accessible constructor")));
  }

  [Fact]
  public void Interface_with_static_abstract_member_reports_error()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IStaticAbstract { static abstract int Zero(); }
class Test { void Run() { var m = A<IStaticAbstract>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK001" && d.GetMessage().Contains("static abstract")));
  }

  [Fact]
  public void Class_with_init_only_property_emits_init_accessor()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class WithInit { public virtual string Name { get; init; } = ""x""; }
class Test { void Run() { var m = A<WithInit>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("init {"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Interface_with_init_only_property_emits_init_accessor()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IWithInit { string Name { get; init; } }
class Test { void Run() { var m = A<IWithInit>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("init {"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Class_with_required_members_compiles()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class WithRequired { public required string Name { get; set; } public virtual int M() => 1; }
class Test { void Run() { var m = A<WithRequired>(); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("SetsRequiredMembers"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Wrap_with_generic_method_compiles()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IGenericWrapped { T Get<T>(); int M(); }
class Impl : IGenericWrapped { public T Get<T>() => default!; public int M() => 1; }
class Test { void Run() { var w = Wrap<IGenericWrapped>(new Impl()); } }
";
    var output = RunGenerator(source);
    Assert(() => output.Contains("T Get<T>()"));

    CompileGeneratedSource(source);
  }

  [Fact]
  public void Non_virtual_arrangement_on_A_mock_reports_MOCK002_error()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class NonVirtualService { public int Compute() => 1; }
class Test { void Run() { var m = A<NonVirtualService>(x => x.Compute().Returns(5)); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK002" && d.Severity == DiagnosticSeverity.Error));
  }

  [Fact]
  public void Non_virtual_standalone_arrangement_reports_MOCK002_error()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class NonVirtualService { public int Compute() => 1; }
class Test { void Run() { var m = A<NonVirtualService>(); m.Compute().Returns(5); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK002" && d.Severity == DiagnosticSeverity.Error));
  }

  [Fact]
  public void Real_object_call_in_nested_lambda_does_not_report_MOCK002()
  {
    var source = @"
using System.Collections.Generic;
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class NonVirtualService { public virtual void Log(string s) { } }
class Test
{
  void Run()
  {
    var list = new List<string>();
    var m = A<NonVirtualService>(x => When(() => x.Log(string.Empty)).Does(_ => list.Add(""x"")));
  }
}
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => !diagnostics.Any(d => d.Id == "MOCK002"));
  }

  [Fact]
  public void Ref_out_method_with_matcher_reports_MOCK003()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface ITryGet { bool TryGet(string key, out string value); }
class Test { void Run() { var m = A<ITryGet>(x => x.TryGet(Any<string>(), out _).Returns(true)); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK003" && d.GetMessage().Contains("ref/out/in")));
  }

  [Fact]
  public void Ref_out_method_without_matcher_does_not_report_MOCK003()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface ITryGet { bool TryGet(string key, out string value); }
class Test { void Run() { var m = A<ITryGet>(x => x.TryGet(""k"", out _).Returns(true)); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => !diagnostics.Any(d => d.Id == "MOCK003"));
  }

  [Fact]
  public void Class_with_explicit_interface_implementation_reports_MOCK005()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IExplicit { int Value(); }
public class ExplicitImpl : IExplicit { int IExplicit.Value() => 1; }
class Test { void Run() { var m = A<ExplicitImpl>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK005" && d.GetMessage().Contains("IExplicit.Value")));
  }

  [Fact]
  public void Class_without_explicit_interface_implementation_does_not_report_MOCK005()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IExplicit { int Value(); }
public class ImplicitImpl : IExplicit { public int Value() => 1; }
class Test { void Run() { var m = A<ImplicitImpl>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => !diagnostics.Any(d => d.Id == "MOCK005"));
  }

  // ── helpers ────────────────────────────────────────────────────────────────

  private static string RunGenerator(string source)
  {
    var (output, _) = RunGeneratorWithDiagnostics(source);
    return output;
  }

  private static void CompileGeneratedSource(string source)
  {
    var generated = RunGenerator(source);

    var references = new List<MetadataReference>
    {
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Assertive.Mocking.Mock).Assembly.Location),
    };

    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    {
      if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location) && !references.Any(r => r.Display == asm.Location))
      {
        references.Add(MetadataReference.CreateFromFile(asm.Location));
      }
    }

    var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
    var allTrees = new List<SyntaxTree>
    {
      CSharpSyntaxTree.ParseText(source, parseOptions),
    };
    allTrees.Add(CSharpSyntaxTree.ParseText(generated, parseOptions));

    var compilation = CSharpCompilation.Create(
      "CompiledTestAssembly",
      allTrees,
      references,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        .WithNullableContextOptions(NullableContextOptions.Enable));

    var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    if (diagnostics.Count == 0) return;
    var messages = string.Join("\n", diagnostics.Select(d => d.ToString()));
    Assert(() => false, $"Expected no compilation errors, but got {diagnostics.Count} error(s):\n{messages}\n\nGenerated source:\n{generated}");
  }

  private static (string Output, IReadOnlyList<Diagnostic> Diagnostics) RunGeneratorWithDiagnostics(string source)
  {
    var references = new List<MetadataReference>
    {
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Assertive.Mocking.Mock).Assembly.Location),
    };

    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    {
      if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location) && references.All(r => r.Display != asm.Location))
      {
        references.Add(MetadataReference.CreateFromFile(asm.Location));
      }
    }

    var compilation = CSharpCompilation.Create(
      "TestAssembly",
      new[] { CSharpSyntaxTree.ParseText(source) },
      references,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    var generator = new Assertive.Mocking.Generators.MockGenerator();
    var driver = CSharpGeneratorDriver.Create(generator);
    driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
    var result = driver.GetRunResult();

    var generatedSource = string.Join("\n", result.GeneratedTrees.Select(t => t.GetText().ToString()));
    var diags = result.Diagnostics.ToList();
    return (generatedSource, diags);
  }
}
