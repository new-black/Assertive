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
