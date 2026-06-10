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

      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
      {
        return null;
      }

      // First parameter must be the assertion expression; this also excludes the DSL's
      // snapshot overload (first parameter is `object`).
      if (method.Parameters.Length == 0
          || method.Parameters[0].Type is not INamedTypeSymbol { Name: "Expression", Arity: 1 })
      {
        return null;
      }

      var isAssertThat = method is { Name: "That", ContainingType: { Name: "Assert", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } } };
      var isDslAssert = method is { Name: "Assert", ContainingType: { Name: "DSL", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } } };

      ThatOverload? overload = null;
      WrapperModel? wrapper = null;

      if (isAssertThat || isDslAssert)
      {
        overload = ClassifyOverload(method);
      }
      else if (HasAssertionWrapperAttribute(method))
      {
        wrapper = AnalyzeWrapper(method, ctx.SemanticModel.Compilation);
      }

      if (overload == null && wrapper == null)
      {
        return null;
      }

      if (invocation.ArgumentList.Arguments.Count == 0
          || invocation.ArgumentList.Arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } body, ParameterList.Parameters.Count: 0 })
      {
        return null;
      }

      // Wrapper call sites must pass remaining arguments positionally for the interceptor's
      // straight forwarding to be faithful.
      if (wrapper != null && invocation.ArgumentList.Arguments.Any(a => a.NameColon != null))
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

        // Extension methods called instance-style (`xs.First()`) would need the extension's
        // namespace imported in the generated file; resolving that faithfully is out of
        // scope for the slice.
        if (node is InvocationExpressionSyntax innerCall
            && ctx.SemanticModel.GetSymbolInfo(innerCall, ct).Symbol is IMethodSymbol { ReducedFrom: not null })
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
        Overload = overload ?? ThatOverload.Plain,
        Wrapper = wrapper,
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

    private static bool HasAssertionWrapperAttribute(IMethodSymbol method)
    {
      foreach (var attribute in method.GetAttributes())
      {
        if (attribute.AttributeClass is { Name: "AssertionWrapperAttribute", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } })
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Validates an [AssertionWrapper] method and locates its paired AssertionHandle
    /// overload: same name, same staticness, same parameters except the first, and
    /// accessible from generated code (which lives outside the declaring type).
    /// </summary>
    private static WrapperModel? AnalyzeWrapper(IMethodSymbol method, Compilation compilation)
    {
      if (method.IsGenericMethod
          || method.ContainingType is not { IsGenericType: false, IsFileLocal: false } containingType
          || !compilation.IsSymbolAccessibleWithin(containingType, compilation.Assembly))
      {
        return null;
      }

      foreach (var parameter in method.Parameters)
      {
        if (parameter.RefKind != RefKind.None || parameter.IsParams)
        {
          return null;
        }
      }

      for (var i = 1; i < method.Parameters.Length; i++)
      {
        if (!IsUsableType(method.Parameters[i].Type, compilation))
        {
          return null;
        }
      }

      if (!method.ReturnsVoid && !IsUsableType(method.ReturnType, compilation))
      {
        return null;
      }

      IMethodSymbol? handleOverload = null;

      foreach (var member in containingType.GetMembers(method.Name))
      {
        if (member is IMethodSymbol candidate
            && !SymbolEqualityComparer.Default.Equals(candidate, method)
            && candidate.IsStatic == method.IsStatic
            && !candidate.IsGenericMethod
            && candidate.Parameters.Length == method.Parameters.Length
            && candidate.Parameters[0].Type is INamedTypeSymbol { Name: "AssertionHandle", ContainingNamespace: { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true } }
            && RemainingParametersMatch(candidate, method)
            && compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly))
        {
          handleOverload = candidate;
          break;
        }
      }

      if (handleOverload == null)
      {
        return null;
      }

      var model = new WrapperModel
      {
        MethodName = method.Name,
        ContainingTypeFqn = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        IsStatic = method.IsStatic,
        ReturnTypeFqn = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
      };

      for (var i = 1; i < method.Parameters.Length; i++)
      {
        model.ExtraParameterTypes.Add(method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
      }

      return model;
    }

    private static bool RemainingParametersMatch(IMethodSymbol candidate, IMethodSymbol original)
    {
      for (var i = 1; i < original.Parameters.Length; i++)
      {
        if (!SymbolEqualityComparer.Default.Equals(candidate.Parameters[i].Type, original.Parameters[i].Type)
            || candidate.Parameters[i].RefKind != RefKind.None)
        {
          return false;
        }
      }

      return true;
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
