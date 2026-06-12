using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class ClassificationContext
  {
    public ClassificationContext(
      GeneratorSyntaxContext syntax,
      InterceptedCall call,
      OperandCompiler compiler,
      CancellationToken ct,
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings = null,
      Func<ExpressionSyntax, string>? display = null)
    {
      Syntax = syntax;
      Call = call;
      Compiler = compiler;
      Ct = ct;
      Bindings = bindings;
      Display = display;
    }

    public GeneratorSyntaxContext Syntax { get; }
    public InterceptedCall Call { get; }
    public OperandCompiler Compiler { get; }
    public CancellationToken Ct { get; }
    public IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? Bindings { get; }
    public Func<ExpressionSyntax, string>? Display { get; }
    public SemanticModel Model => Syntax.SemanticModel;

    public ClassificationContext WithCall(InterceptedCall call)
      => new ClassificationContext(Syntax, call, Compiler, Ct, Bindings, Display);

    public ClassificationContext WithBindings(IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
      => new ClassificationContext(Syntax, Call, Compiler, Ct, bindings, Display);

    public ClassificationContext WithDisplay(Func<ExpressionSyntax, string>? display)
      => new ClassificationContext(Syntax, Call, Compiler, Ct, Bindings, display);
  }
}
