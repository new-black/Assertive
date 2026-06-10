using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  /// <summary>
  /// Decides whether an assertion call site is whitelisted for the equality slice, and if
  /// so produces everything emission needs. The whitelist is deliberately conservative: any
  /// doubt means "don't intercept", which leaves the call site on the degraded delegate
  /// path (source text only, no decomposition).
  ///
  /// Whitelisted forms: a () => lambda whose body is a binary ==/!= comparison, an
  /// .Equals(x) call, or a ReferenceEquals(x, y) call, with outer !-negations of each.
  /// Each operand is compiled independently: typed C# when every name involved is nameable
  /// from the generated file, otherwise a reflective fallback that reads the closure and
  /// resolves members at runtime (private nested types, private members on `this`).
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

      // First parameter must be the assertion delegate; this also excludes the DSL's
      // snapshot overload (first parameter is `object`) and the AssertionHandle overloads.
      if (method.Parameters.Length == 0
          || method.Parameters[0].Type is not INamedTypeSymbol { Name: "Func", Arity: 1 })
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

      // Arguments must be positional for the interceptor's straight forwarding to be faithful.
      if (invocation.ArgumentList.Arguments.Any(a => a.NameColon != null))
      {
        return null;
      }

      // Recognize the supported forms, peeling outer !-negations.
      var outerNegated = false;
      var core = StripParens(body);

      while (core is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression))
      {
        outerNegated = !outerNegated;
        core = StripParens(negation.Operand);
      }

      ExpressionSyntax leftOperand;
      ExpressionSyntax rightOperand;
      var kind = InterceptionKind.Equality;
      var negated = outerNegated;

      if (core is BinaryExpressionSyntax comparison
          && comparison.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
      {
        leftOperand = comparison.Left;
        rightOperand = comparison.Right;
        negated = (comparison.IsKind(SyntaxKind.NotEqualsExpression)) ^ outerNegated;
      }
      else if (core is InvocationExpressionSyntax
               {
                 Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" } equalsAccess,
                 ArgumentList.Arguments.Count: 1
               } equalsCall
               && ctx.SemanticModel.GetSymbolInfo(equalsCall, ct).Symbol is IMethodSymbol
               {
                 Name: "Equals", IsStatic: false, ReturnType.SpecialType: SpecialType.System_Boolean, Parameters.Length: 1
               })
      {
        leftOperand = equalsAccess.Expression;
        rightOperand = equalsCall.ArgumentList.Arguments[0].Expression;
      }
      else if (core is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 2 } referenceEqualsCall
               && ctx.SemanticModel.GetSymbolInfo(referenceEqualsCall, ct).Symbol is IMethodSymbol
               {
                 Name: "ReferenceEquals", IsStatic: true, Parameters.Length: 2, ContainingType.SpecialType: SpecialType.System_Object
               }
               && referenceEqualsCall.ArgumentList.Arguments.All(a => a.NameColon == null))
      {
        kind = InterceptionKind.ReferenceEquals;
        leftOperand = referenceEqualsCall.ArgumentList.Arguments[0].Expression;
        rightOperand = referenceEqualsCall.ArgumentList.Arguments[1].Expression;
      }
      else
      {
        return null;
      }

      var left = StripParens(leftOperand);
      var right = StripParens(rightOperand);

      // Shapes that route to other patterns today (NullPattern, LengthPattern) stay on the
      // degraded path until those patterns are ported.
      if (kind == InterceptionKind.Equality
          && (IsNullOrDefaultLiteral(left) || IsNullOrDefaultLiteral(right)
              || IsLengthOrCountAccess(left) || IsLengthOrCountAccess(right)))
      {
        return null;
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
        Overload = overload ?? ThatOverload.MessageContext,
        Wrapper = wrapper,
        BodySource = body.ToString(),
        Kind = kind,
        Negated = negated,
        RightIsConstant = right is LiteralExpressionSyntax,
        LeftDisplay = leftOperand.ToString(),
        RightDisplay = rightOperand.ToString(),
      };

      var compiler = new OperandCompiler(ctx.SemanticModel, body, call, ct);

      call.LeftSource = compiler.Compile(leftOperand)!;
      call.RightSource = compiler.Compile(rightOperand)!;

      if (call.LeftSource == null || call.RightSource == null)
      {
        return null;
      }

      return call;
    }

    /// <summary>
    /// Compiles a single operand expression into C# for the generated failure path.
    /// First tries the typed strategy (paste the source, fully qualify type references,
    /// read captured locals from the closure with a typed cast); when any name involved
    /// is not nameable from the generated file, falls back to the reflective strategy
    /// (object-typed values, members resolved at runtime by name). Captured locals are
    /// registered on the call as a side effect of successful compilation.
    /// </summary>
    private sealed class OperandCompiler
    {
      private readonly SemanticModel _model;
      private readonly Compilation _compilation;
      private readonly SyntaxNode _body;
      private readonly InterceptedCall _call;
      private readonly CancellationToken _ct;

      private const string Runtime = "global::Assertive.Runtime.GeneratedAssert";
      private const string CapturedThis = Runtime + ".GetCapturedThis(__assertion)";

      public OperandCompiler(SemanticModel model, SyntaxNode body, InterceptedCall call, CancellationToken ct)
      {
        _model = model;
        _compilation = model.Compilation;
        _body = body;
        _call = call;
        _ct = ct;
      }

      public string? Compile(ExpressionSyntax operand)
      {
        var typed = CompileTyped(operand);

        if (typed != null)
        {
          return typed;
        }

        var captures = new List<(string Name, string? Type)>();
        var reflective = CompileReflective(operand, captures);

        if (reflective != null)
        {
          Commit(captures);
        }

        return reflective;
      }

      private string? CompileTyped(ExpressionSyntax operand)
      {
        foreach (var node in operand.DescendantNodesAndSelf())
        {
          if (node is ThisExpressionSyntax or BaseExpressionSyntax or QueryExpressionSyntax or AnonymousObjectCreationExpressionSyntax)
          {
            return null;
          }

          // Extension methods called instance-style (`xs.First()`) would need the extension's
          // namespace imported in the generated file; resolving that faithfully is out of scope.
          if (node is InvocationExpressionSyntax innerCall
              && _model.GetSymbolInfo(innerCall, _ct).Symbol is IMethodSymbol { ReducedFrom: not null })
          {
            return null;
          }
        }

        // Classify every free simple name and collect type-qualifier rewrites (so
        // `x == StringComparison.Ordinal` emits `global::System.StringComparison.Ordinal`).
        var rewrites = new List<(TextSpanStart Start, int Length, string Replacement)>();
        var captures = new List<(string Name, string? Type)>();

        foreach (var name in operand.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
          // Names inside an already-rewritten span (type arguments of a rewritten generic
          // name, say) are covered by the outer rewrite; document order visits outers first.
          if (rewrites.Any(r => name.SpanStart >= r.Start.Value && name.Span.End <= r.Start.Value + r.Length))
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

          var symbol = _model.GetSymbolInfo(name, _ct).Symbol;

          switch (symbol)
          {
            case INamespaceSymbol ns:
              rewrites.Add((new TextSpanStart(name.SpanStart), name.Span.Length,
                ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
              continue;

            case ITypeSymbol type:
              // A type that can't be named from the generated file (private nested, say)
              // fails the typed strategy; the reflective one may still handle it.
              if (!IsUsableType(type, _compilation))
              {
                return null;
              }

              rewrites.Add((new TextSpanStart(name.SpanStart), name.Span.Length,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
              continue;

            case ILocalSymbol local when name is IdentifierNameSyntax:
              // Const locals have no closure field (they're baked in as constants).
              if (local.IsConst || !IsUsableType(local.Type, _compilation))
              {
                return null;
              }

              captures.Add((local.Name, local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
              continue;

            case IParameterSymbol param when name is IdentifierNameSyntax:
              // Parameters declared inside the assertion body (nested lambdas) are bound by
              // the pasted code itself; parameters of enclosing methods/lambdas are captured
              // like locals.
              if (IsDeclaredWithin(param, _body))
              {
                continue;
              }

              if (!IsUsableType(param.Type, _compilation))
              {
                return null;
              }

              captures.Add((param.Name, param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
              continue;

            default:
              // Fields/properties on `this`, statics via `using static`, local functions,
              // method groups, nameof, range variables, anything unresolved: not typed-
              // compilable; the reflective strategy may still apply.
              return null;
          }
        }

        Commit(captures);

        return SourceWithRewrites(operand, rewrites);
      }

      /// <summary>
      /// Compiles an operand to an object-typed expression that evaluates it through the
      /// closure and runtime reflection. Supported shapes: literals, captured locals and
      /// parameters (of any type), `this` and its fields/properties/method calls (any
      /// accessibility), member access chains, and static fields/properties including enum
      /// constants on types reachable through a nameable ancestor. Anything else fails.
      /// </summary>
      private string? CompileReflective(ExpressionSyntax expression, List<(string Name, string? Type)> captures)
      {
        expression = StripParens(expression);

        switch (expression)
        {
          case LiteralExpressionSyntax literal:
            return $"(object)({literal})";

          case ThisExpressionSyntax:
            return CapturedThis;

          case IdentifierNameSyntax identifier:
            switch (_model.GetSymbolInfo(identifier, _ct).Symbol)
            {
              case ILocalSymbol { IsConst: false } local:
                AddCapture(captures, local.Name, local.Type);
                return local.Name;

              case IParameterSymbol param when !IsDeclaredWithin(param, _body):
                AddCapture(captures, param.Name, param.Type);
                return param.Name;

              case IFieldSymbol { IsStatic: false } field:
                return $"{Runtime}.GetMemberValue({CapturedThis}, {Quote(MetadataName(field))})";

              case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                return $"{Runtime}.GetMemberValue({CapturedThis}, {Quote(property.Name)})";

              case IFieldSymbol { IsStatic: true } staticField:
                return StaticMember(staticField.ContainingType, MetadataName(staticField));

              case IPropertySymbol { IsStatic: true } staticProperty:
                return StaticMember(staticProperty.ContainingType, staticProperty.Name);

              default:
                return null;
            }

          case MemberAccessExpressionSyntax memberAccess when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
            switch (_model.GetSymbolInfo(memberAccess, _ct).Symbol)
            {
              case IFieldSymbol { IsStatic: true } staticField:
                // Includes enum constants like MyEnum.B on a private nested enum.
                return StaticMember(staticField.ContainingType, MetadataName(staticField));

              case IPropertySymbol { IsStatic: true } staticProperty:
                return StaticMember(staticProperty.ContainingType, staticProperty.Name);

              case IFieldSymbol { IsStatic: false } field:
                return CompileReflective(memberAccess.Expression, captures) is { } fieldReceiver
                  ? $"{Runtime}.GetMemberValue({fieldReceiver}, {Quote(MetadataName(field))})"
                  : null;

              case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                return CompileReflective(memberAccess.Expression, captures) is { } propertyReceiver
                  ? $"{Runtime}.GetMemberValue({propertyReceiver}, {Quote(property.Name)})"
                  : null;

              default:
                return null;
            }

          case InvocationExpressionSyntax invocation:
          {
            if (_model.GetSymbolInfo(invocation, _ct).Symbol is not IMethodSymbol method
                || method.IsStatic
                || method.IsGenericMethod
                || method.ReducedFrom != null
                || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
                || invocation.ArgumentList.Arguments.Count != method.Parameters.Length
                || invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
                || !HasSingleOverload(method))
            {
              return null;
            }

            var receiver = invocation.Expression switch
            {
              MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                => CompileReflective(access.Expression, captures),
              IdentifierNameSyntax => CapturedThis, // implicit this
              _ => null,
            };

            if (receiver == null)
            {
              return null;
            }

            var arguments = new List<string>();

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
              if (CompileReflective(argument.Expression, captures) is not { } compiled)
              {
                return null;
              }

              arguments.Add(compiled);
            }

            var argumentArray = arguments.Count == 0
              ? "global::System.Array.Empty<object>()"
              : $"new object[] {{ {string.Join(", ", arguments)} }}";

            return $"{Runtime}.InvokeInstance({receiver}, {Quote(method.Name)}, {argumentArray})";
          }

          default:
            return null;
        }
      }

      /// <summary>
      /// A typeof()/GetNestedType() chain for a (possibly unnameable) type: the outermost
      /// nameable ancestor anchors a typeof(), and each unnameable nesting level is resolved
      /// by metadata name at runtime. Null when no nameable anchor exists.
      /// </summary>
      private string? BuildTypeAccessor(INamedTypeSymbol type)
      {
        if (IsUsableType(type, _compilation))
        {
          return $"typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
        }

        if (type.IsGenericType || type.IsFileLocal || type.ContainingType is not { } parent)
        {
          return null;
        }

        return BuildTypeAccessor(parent) is { } parentAccessor
          ? $"{Runtime}.GetNestedType({parentAccessor}, {Quote(type.MetadataName)})"
          : null;
      }

      private string? StaticMember(INamedTypeSymbol containingType, string memberName)
      {
        return BuildTypeAccessor(containingType) is { } typeAccessor
          ? $"{Runtime}.GetStaticMemberValue({typeAccessor}, {Quote(memberName)})"
          : null;
      }

      /// <summary>
      /// Runtime resolution is by name + parameter count, so interception requires that
      /// combination to be unambiguous across the type hierarchy.
      /// </summary>
      private static bool HasSingleOverload(IMethodSymbol method)
      {
        var count = 0;

        for (var type = method.ContainingType; type != null; type = type.BaseType)
        {
          foreach (var member in type.GetMembers(method.Name))
          {
            if (member is IMethodSymbol { IsStatic: false } candidate
                && candidate.Parameters.Length == method.Parameters.Length
                && candidate.OverriddenMethod == null)
            {
              count++;
            }
          }
        }

        return count == 1;
      }

      /// <summary>Tuple elements are read through their underlying ItemN field.</summary>
      private static string MetadataName(IFieldSymbol field)
      {
        return (field.CorrespondingTupleField ?? field).Name;
      }

      private void AddCapture(List<(string Name, string? Type)> captures, string name, ITypeSymbol type)
      {
        var typeFqn = IsUsableType(type, _compilation)
          ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          : null;

        captures.Add((name, typeFqn));
      }

      private void Commit(List<(string Name, string? Type)> captures)
      {
        foreach (var capture in captures)
        {
          var existing = _call.CapturedLocals.FindIndex(l => l.Name == capture.Name);

          if (existing < 0)
          {
            _call.CapturedLocals.Add(capture);
          }
          else if (_call.CapturedLocals[existing].Type == null && capture.Type != null)
          {
            _call.CapturedLocals[existing] = capture;
          }
        }
      }

      private static string Quote(string text) => SymbolDisplay.FormatLiteral(text, quote: true);
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
      // That(Func<bool>, object? message, Func<object?>? context, [CAE] string, [CAE] string)
      // and That(Func<bool>, Func<object?> context, [CAE] string, [CAE] string).
      return method.Parameters.Length switch
      {
        4 when method.Parameters[1].Type is INamedTypeSymbol { Name: "Func" } => ThatOverload.Context,
        5 when method.Parameters[1].Type.SpecialType == SpecialType.System_Object => ThatOverload.MessageContext,
        _ => null,
      };
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
