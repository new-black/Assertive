using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  /// <summary>
  /// Decides whether an Assert.That call site is whitelisted for the equality slice, and if
  /// so produces everything emission needs. The whitelist is deliberately conservative: any
  /// doubt means "don't intercept", which leaves the call site byte-for-byte on the existing
  /// runtime pipeline.
  ///
  /// Whitelisted: a () => lambda whose body is a binary ==/!= that routes to
  /// EqualsPattern/NotEqualsPattern today (no null/default literals, no Length/Count member
  /// comparison, not negated), where every free identifier is a captured local or parameter
  /// of a nameable type, or a type/namespace qualifier (which gets fully qualified in the
  /// emitted code).
  /// </summary>
  internal static class CallSiteAnalyzer
  {
    public static InterceptedCall? Analyze(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
          || method.Name != "That"
          || method.ContainingType is not { Name: "Assert", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } })
      {
        return null;
      }

      var overload = ClassifyOverload(method);

      if (overload == null)
      {
        return null;
      }

      if (invocation.ArgumentList.Arguments.Count == 0
          || invocation.ArgumentList.Arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } body, ParameterList.Parameters.Count: 0 })
      {
        return null;
      }

      var comparison = StripParens(body) as BinaryExpressionSyntax;

      if (comparison == null
          || comparison.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
      {
        return null;
      }

      var left = StripParens(comparison.Left);
      var right = StripParens(comparison.Right);

      // Shapes that route to other patterns today (NullPattern, LengthPattern) stay on the
      // existing pipeline so their messages are untouched by the slice.
      if (IsNullOrDefaultLiteral(left) || IsNullOrDefaultLiteral(right)
          || IsLengthOrCountAccess(left) || IsLengthOrCountAccess(right))
      {
        return null;
      }

      // Constructs the slice doesn't support (or that warrant extra caution) anywhere in the body.
      foreach (var node in body.DescendantNodesAndSelf())
      {
        if (node is ThisExpressionSyntax or BaseExpressionSyntax or QueryExpressionSyntax or AnonymousObjectCreationExpressionSyntax)
        {
          return null;
        }
      }

      var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);

      if (location == null)
      {
        return null;
      }

      var call = new InterceptedCall
      {
        LocationVersion = location.Version,
        LocationData = location.Data,
        DisplayLocation = location.GetDisplayLocation(),
        FilePath = invocation.SyntaxTree.FilePath,
        Overload = overload.Value,
      };

      // Classify every free simple name in the body and collect type-qualifier rewrites
      // (so `x == StringComparison.Ordinal` emits `global::System.StringComparison.Ordinal`).
      var typeRewrites = new List<(TextSpanStart Start, int Length, string Replacement)>();

      foreach (var name in body.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
      {
        // Names inside an already-rewritten span (type arguments of a rewritten generic
        // name, say) are covered by the outer rewrite; document order visits outers first.
        if (typeRewrites.Any(r => name.SpanStart >= r.Start.Value && name.Span.End <= r.Start.Value + r.Length))
        {
          continue;
        }

        // Member names (`Length` in `x.Length`, `Ordinal` in `StringComparison.Ordinal`):
        // the receiver/qualifier determines them.
        if ((name.Parent is MemberAccessExpressionSyntax ma && ma.Name == name)
            || (name.Parent is QualifiedNameSyntax qn && qn.Right == name))
        {
          continue;
        }

        var symbol = ctx.SemanticModel.GetSymbolInfo(name, ct).Symbol;

        switch (symbol)
        {
          case INamespaceOrTypeSymbol namespaceOrType:
            typeRewrites.Add((new TextSpanStart(name.SpanStart), name.Span.Length,
              namespaceOrType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            continue;

          case ILocalSymbol local when name is IdentifierNameSyntax:
            // Const locals have no closure field (they're baked into the tree as constants).
            if (local.IsConst || !IsUsableType(local.Type, ctx.SemanticModel.Compilation))
            {
              return null;
            }

            AddCapturedLocal(call, local.Name, local.Type);
            continue;

          case IParameterSymbol param when name is IdentifierNameSyntax:
            // Parameters declared inside the assertion body (nested lambdas) are bound by
            // the pasted code itself; parameters of enclosing methods/lambdas are captured
            // like locals.
            if (IsDeclaredWithin(param, body))
            {
              continue;
            }

            if (!IsUsableType(param.Type, ctx.SemanticModel.Compilation))
            {
              return null;
            }

            AddCapturedLocal(call, param.Name, param.Type);
            continue;

          default:
            // Fields/properties on `this`, statics via `using static`, local functions,
            // method groups, nameof, range variables, anything unresolved: out of scope
            // for the slice — don't intercept.
            return null;
        }
      }

      call.LeftSource = SourceWithRewrites(comparison.Left, typeRewrites);
      call.RightSource = SourceWithRewrites(comparison.Right, typeRewrites);
      call.Operator = comparison.OperatorToken.Text;

      return call;
    }

    private static ThatOverload? ClassifyOverload(IMethodSymbol method)
    {
      return method.Parameters.Length switch
      {
        1 => ThatOverload.Plain,
        2 when method.Parameters[1].Type.SpecialType == SpecialType.System_Object => ThatOverload.Message,
        2 => ThatOverload.Context,
        3 => ThatOverload.MessageContext,
        _ => null,
      };
    }

    private static void AddCapturedLocal(InterceptedCall call, string name, ITypeSymbol type)
    {
      if (call.CapturedLocals.Any(l => l.Name == name))
      {
        return;
      }

      call.CapturedLocals.Add((name, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private static bool IsDeclaredWithin(ISymbol symbol, SyntaxNode scope)
    {
      foreach (var reference in symbol.DeclaringSyntaxReferences)
      {
        if (reference.SyntaxTree == scope.SyntaxTree && scope.Span.Contains(reference.Span))
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Whether the type can be written down (for the closure-value cast) and used from the
    /// generated file, which lives in the consumer assembly but outside any type/file scope.
    /// </summary>
    private static bool IsUsableType(ITypeSymbol type, Compilation compilation)
    {
      switch (type)
      {
        case ITypeParameterSymbol:
          return false;
        case IPointerTypeSymbol:
        case IFunctionPointerTypeSymbol:
          return false;
        case IArrayTypeSymbol array:
          return IsUsableType(array.ElementType, compilation);
        case INamedTypeSymbol named:
          if (named.IsAnonymousType || named.IsFileLocal || named.IsRefLikeType || named.TypeKind == TypeKind.Dynamic)
          {
            return false;
          }

          if (!compilation.IsSymbolAccessibleWithin(named, compilation.Assembly))
          {
            return false;
          }

          foreach (var typeArgument in named.TypeArguments)
          {
            if (!IsUsableType(typeArgument, compilation))
            {
              return false;
            }
          }

          return true;
        default:
          return false;
      }
    }

    private static bool IsNullOrDefaultLiteral(ExpressionSyntax expression)
    {
      return expression.IsKind(SyntaxKind.NullLiteralExpression)
             || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
             || expression is DefaultExpressionSyntax;
    }

    private static bool IsLengthOrCountAccess(ExpressionSyntax expression)
    {
      return expression switch
      {
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" } => true,
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" or "LongCount" } } => true,
        _ => false,
      };
    }

    private static ExpressionSyntax StripParens(ExpressionSyntax expression)
    {
      while (expression is ParenthesizedExpressionSyntax p)
      {
        expression = p.Expression;
      }

      return expression;
    }

    private static string SourceWithRewrites(ExpressionSyntax operand, List<(TextSpanStart Start, int Length, string Replacement)> rewrites)
    {
      var text = operand.ToString();
      var offset = operand.SpanStart;

      foreach (var rewrite in rewrites
                 .Where(r => r.Start.Value >= operand.SpanStart && r.Start.Value + r.Length <= operand.Span.End)
                 .OrderByDescending(r => r.Start.Value))
      {
        var relative = rewrite.Start.Value - offset;
        text = text.Substring(0, relative) + rewrite.Replacement + text.Substring(relative + rewrite.Length);
      }

      return text;
    }
  }

  /// <summary>Absolute span start; a named wrapper to keep tuple members readable.</summary>
  internal readonly struct TextSpanStart
  {
    public TextSpanStart(int value) => Value = value;
    public int Value { get; }
  }
}
