using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Assertive.DSL;

public class GeneratorSnapshotTests
{
  [Fact]
  public void Generator_produces_mock_class_for_interface()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IFoo { string Bar(); }
class Test { void M() { var f = A<IFoo>(); } }
";
    var output = RunGenerator(source);

    Assert(() => output.Contains("class Mock_IFoo"));
    Assert(() => output.Contains("public string Bar()"));
  }

  [Fact]
  public void Generator_produces_override_for_class_mock()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public class MyService { public virtual string Do() => ""real""; }
class Test { void M() { var f = A<MyService>(); } }
";
    var output = RunGenerator(source);

    Assert(() => output.Contains("public override"));
    Assert(() => output.Contains("class Mock_MyService"));
  }

  [Fact]
  public void Generator_emits_MOCK001_for_sealed_type()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public sealed class Sealed {}
class Test { void M() { var f = A<Sealed>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK001"));
  }

  [Fact]
  public void Generator_registers_mock_in_module_initializer()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IBar { int Count(); }
class Test { void M() { var b = A<IBar>(); } }
";
    var output = RunGenerator(source);

    Assert(() => output.Contains("ModuleInitializer"));
    Assert(() => output.Contains("MockFactoryRegistry.Register"));
  }

  [Fact]
  public void Generator_produces_mock_class_for_closed_generic_interface()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IWidget { }
public interface IRepository<T> { T FindById(int id); }
class Test { void M() { var r = A<IRepository<IWidget>>(); } }
";
    var output = RunGenerator(source);

    Assert(() => output.Contains("Mock_IRepositoryIWidget"));
    Assert(() => output.Contains("public global::IWidget FindById("));
  }

  [Fact]
  public void Generator_emits_MOCK001_for_open_generic_interface()
  {
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IRepository<T> { T FindById(int id); }
class Test { void M<T>() { var r = A<IRepository<T>>(); } }
";
    var (_, diagnostics) = RunGeneratorWithDiagnostics(source);

    Assert(() => diagnostics.Any(d => d.Id == "MOCK001"));
  }

  [Fact]
  public void Generator_emits_explicit_stub_for_return_type_conflict_from_base_interface()
  {
    // IResult<T> inherits IResult; both declare Get() with different return types.
    // The derived version must be the tracked mock method; the base version must be
    // an explicit interface implementation so the class compiles.
    var source = @"
using Assertive.Mocking;
using static Assertive.Mocking.Mock;
public interface IResult { object Get(); }
public interface IResult<T> : IResult { new T Get(); }
class Test { void M() { var r = A<IResult<string>>(); } }
";
    var output = RunGenerator(source);

    // Mock class is generated
    Assert(() => output.Contains("Mock_IResultSystem_String") || output.Contains("Mock_IResultstring"));
    // The tracked method uses the derived return type (Roslyn uses keyword form "string" in FullyQualifiedFormat)
    Assert(() => output.Contains("public string Get()"));
    // The base interface conflict is resolved with an explicit implementation stub
    Assert(() => output.Contains("global::IResult.Get()"));
  }

  // ── helpers ────────────────────────────────────────────────────────────────

  private static string RunGenerator(string source)
  {
    var (output, _) = RunGeneratorWithDiagnostics(source);
    return output;
  }

  private static (string Output, IReadOnlyList<Diagnostic> Diagnostics) RunGeneratorWithDiagnostics(string source)
  {
    var compilation = CSharpCompilation.Create(
      "TestAssembly",
      new[] { CSharpSyntaxTree.ParseText(source) },
      new[]
      {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Assertive.Mocking.Mock).Assembly.Location),
      },
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
