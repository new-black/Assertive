using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  /// <summary>
  /// Compiles a single operand expression into C# for the generated failure path.
  /// First tries the typed strategy (paste the source, fully qualify type references and
  /// reduced extension calls, read captured locals from the closure with a typed cast);
  /// when any name involved is not nameable or a member is not accessible from the
  /// generated file, falls back to the reflective strategy (object-typed values, members
  /// resolved at runtime by name). Captured locals are registered on the call as a side
  /// effect of successful compilation.
  /// </summary>
  internal sealed class OperandCompiler
  {
    private readonly SemanticModel _model;
    private readonly Compilation _compilation;
    private readonly SyntaxNode _body;
    private readonly InterceptedCall _call;
    private readonly CancellationToken _ct;

    private const string Runtime = "__A";
    private const string CapturedThis = Runtime + ".GetCapturedThis(__f)";

    public OperandCompiler(SemanticModel model, SyntaxNode body, InterceptedCall call, CancellationToken ct)
    {
      _model = model;
      _compilation = model.Compilation;
      _body = body;
      _call = call;
      _ct = ct;
    }

    /// <summary>Typed strategy only — for code that must keep its static type (LINQ Count with filter).</summary>
    public string? CompileTypedOnly(ExpressionSyntax operand, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings = null)
      => CompileTyped(operand, bindings);

    public string? Compile(ExpressionSyntax operand, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings = null,
      IReadOnlyDictionary<string, string>? designationRenames = null)
    {
      var typed = CompileTyped(operand, bindings, designationRenames);

      if (typed != null)
      {
        return typed;
      }

      var captures = new List<(string Name, string? Type)>();
      var reflective = CompileReflective(operand, captures, bindings);

      if (reflective != null)
      {
        Commit(captures);
      }

      return reflective;
    }

    private string? CompileTyped(ExpressionSyntax operand, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings = null,
      IReadOnlyDictionary<string, string>? designationRenames = null)
    {
      var captures = new List<(string Name, string? Type)>();

      if (!ValidateTypedFragment(operand, captures, bindings))
      {
        return null;
      }

      var rewriter = new TypedRenderRewriter(_model, operand, bindings, _ct, designationRenames);
      var rendered = rewriter.Visit(operand);

      if (rewriter.Failed || rendered == null)
      {
        return null;
      }

      Commit(captures);

      return rendered.ToString();
    }

    /// <summary>
    /// Whether the fragment can be pasted into generated code as-is (modulo the
    /// TypedRenderRewriter's qualification): every node bindable from outside the
    /// declaring type, every type nameable, captured locals/parameters registered.
    /// Shared by operand compilation (expressions) and local-function lifting (whole
    /// declarations).
    /// </summary>
    private bool ValidateTypedFragment(SyntaxNode fragment, List<(string Name, string? Type)> captures,
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
    {
      // DeclarationExpressions (out var x) are only valid inside an enclosing expression;
      // as a standalone compiled operand they produce non-evaluable syntax like "(object)(T x)".
      if (fragment is DeclarationExpressionSyntax)
      {
        return false;
      }

      foreach (var node in fragment.DescendantNodesAndSelf())
      {
        switch (node)
        {
          case ThisExpressionSyntax:
          case BaseExpressionSyntax:
          case QueryExpressionSyntax:
            return false;

          case AwaitExpressionSyntax awaitExpression:
          {
            // Awaits paste fine into the async render callback, but the generated file
            // has no using directives: awaitables whose GetAwaiter is an extension
            // method would not bind there. Task and friends use an instance GetAwaiter.
            if (_model.GetAwaitExpressionInfo(awaitExpression).GetAwaiterMethod is not { IsStatic: false, ReducedFrom: null })
            {
              return false;
            }

            continue;
          }

          case InvocationExpressionSyntax innerCall:
          {
            if (_model.GetSymbolInfo(innerCall, _ct).Symbol is not IMethodSymbol innerMethod)
            {
              continue; // nameof and friends
            }

            // Calls to local functions bind to the lifted declaration; the name walk
            // below performs the lift.
            if (innerMethod.MethodKind == MethodKind.LocalFunction)
            {
              continue;
            }

            if (!IsAccessibleMember(innerMethod))
            {
              return false;
            }

            // Reduced extension calls are rewritten to fully-qualified static calls,
            // which requires a plain member-access receiver and a nameable static class.
            if (innerMethod.ReducedFrom != null
                && (innerCall.Expression is not MemberAccessExpressionSyntax extensionAccess
                    || !extensionAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                    || !CallSiteAnalyzer.IsUsableType(innerMethod.ContainingType, _compilation)))
            {
              return false;
            }

            continue;
          }

          // Pasted member accesses bind outside the declaring type, so the member itself
          // must be accessible from generated code (private/protected members are not).
          case MemberAccessExpressionSyntax memberAccess:
            if (_model.GetSymbolInfo(memberAccess, _ct).Symbol is { } memberSymbol && !IsAccessibleMember(memberSymbol))
            {
              return false;
            }

            continue;

          case ElementAccessExpressionSyntax elementAccess:
            if (_model.GetSymbolInfo(elementAccess, _ct).Symbol is { } indexerSymbol && !IsAccessibleMember(indexerSymbol))
            {
              return false;
            }

            continue;

          case ObjectCreationExpressionSyntax creation:
            if (_model.GetSymbolInfo(creation, _ct).Symbol is { } constructorSymbol && !IsAccessibleMember(constructorSymbol))
            {
              return false;
            }

            continue;
        }
      }

      foreach (var name in fragment.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
      {
        // Member names (`Length` in `x.Length`, `Ordinal` in `StringComparison.Ordinal`):
        // the receiver/qualifier determines them.
        if (IsMemberNamePosition(name))
        {
          continue;
        }

        var symbol = _model.GetSymbolInfo(name, _ct).Symbol;

        switch (symbol)
        {
          case INamespaceSymbol:
            continue;

          case ITypeSymbol type:
            // A type that can't be named from the generated file (private nested, say)
            // fails the typed strategy; the reflective one may still handle it.
            if (!CallSiteAnalyzer.IsUsableType(type, _compilation))
            {
              return false;
            }

            continue;

          case IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction:
            // Local functions are not members of anything callable from generated code,
            // but their declaration is right here in the syntax tree: lift it into the
            // generated scope, where this name then binds to the pasted copy.
            if (!TryLiftLocalFunction(localFunction))
            {
              return false;
            }

            continue;

          case ILocalSymbol local when name is IdentifierNameSyntax:
            // Locals declared inside the fragment (pattern variables, lifted local
            // function bodies) are bound by the pasted code itself.
            if (IsDeclaredWithin(local, fragment))
            {
              continue;
            }

            // Pattern-variable binding from a prior &&-chain conjunct: the TypedRenderRewriter
            // replaces this name with the pre-declared outer variable.
            if (bindings != null && bindings.TryGetValue(local.Name, out var pvBinding)
                && pvBinding.TypedReplacement != null)
            {
              continue;
            }

            // A local declared inside the assertion body but not within this fragment is
            // a pattern variable (or out-var) from another conjunct — it is not a closure
            // field and cannot be read via GetCapturedValue.
            if (IsDeclaredWithin(local, _body))
            {
              return false;
            }

            // Const locals have no closure field (they're baked in as constants).
            if (local.IsConst || !CallSiteAnalyzer.IsUsableType(local.Type, _compilation))
            {
              return false;
            }

            captures.Add((local.Name, local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            continue;

          case IParameterSymbol param when name is IdentifierNameSyntax:
            // Parameters declared inside the compiled fragment (nested lambdas, lifted
            // local functions) are bound by the pasted code itself.
            if (IsDeclaredWithin(param, fragment))
            {
              continue;
            }

            // Lambda parameters of the assertion body that the caller bound to a value
            // (exception-step item/index/candidate) are rewritten to their replacement.
            if (IsDeclaredWithin(param, _body))
            {
              if (bindings != null && bindings.TryGetValue(param.Name, out var binding)
                  && binding.TypedReplacement != null)
              {
                continue;
              }

              return false;
            }

            // Parameters of enclosing methods/lambdas are captured like locals.
            if (!CallSiteAnalyzer.IsUsableType(param.Type, _compilation))
            {
              return false;
            }

            captures.Add((param.Name, param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            continue;

          default:
            // Fields/properties on `this`, statics via `using static`, method groups,
            // range variables, anything unresolved: not typed-compilable; the
            // reflective strategy may still apply.
            return false;
        }
      }

      return true;
    }

    /// <summary>Lift results per local function, memoized by symbol (also breaks recursion cycles).</summary>
    private readonly Dictionary<IMethodSymbol, bool> _liftedLocalFunctions = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Lifts a local function by pasting its (qualification-rewritten) declaration into
    /// the generated reporting scope. Captured locals bind to the capture declarations
    /// the render scope already makes from the closure; calls to other local functions
    /// lift transitively. Validation is the same whitelist as typed operand pasting, so
    /// bodies touching `this` or inaccessible members reject the lift (degradation).
    /// </summary>
    private bool TryLiftLocalFunction(IMethodSymbol symbol)
    {
      if (_liftedLocalFunctions.TryGetValue(symbol, out var lifted))
      {
        return lifted;
      }

      // Optimistic marker: a self-recursive body finding itself mid-lift is fine —
      // the name will bind to the pasted declaration.
      _liftedLocalFunctions[symbol] = true;

      if (symbol.DeclaringSyntaxReferences.Length != 1
          || symbol.DeclaringSyntaxReferences[0].GetSyntax(_ct) is not LocalFunctionStatementSyntax declaration
          || declaration.SyntaxTree != _body.SyntaxTree)
      {
        return _liftedLocalFunctions[symbol] = false;
      }

      var captures = new List<(string Name, string? Type)>();

      if (!ValidateTypedFragment(declaration, captures, bindings: null))
      {
        return _liftedLocalFunctions[symbol] = false;
      }

      var rewriter = new TypedRenderRewriter(_model, declaration, null, _ct);
      var rendered = rewriter.Visit(declaration);

      if (rewriter.Failed || rendered == null)
      {
        return _liftedLocalFunctions[symbol] = false;
      }

      Commit(captures);
      _call.LiftedLocalFunctions.Add(rendered.ToString());

      return true;
    }

    private bool IsAccessibleMember(ISymbol symbol)
    {
      return symbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol)
             || _compilation.IsSymbolAccessibleWithin(symbol, _compilation.Assembly);
    }

    private static bool IsMemberNamePosition(SimpleNameSyntax name)
    {
      return (name.Parent is MemberAccessExpressionSyntax ma && ma.Name == name)
             || (name.Parent is QualifiedNameSyntax qn && qn.Right == name)
             || name.Parent is MemberBindingExpressionSyntax
             // Named tuple elements ((x: 1, y: 2)) and named arguments: the name is
             // not a free-standing reference, it pastes as written.
             || name.Parent is NameColonSyntax;
    }

    /// <summary>
    /// Renders typed operand code: type/namespace qualifiers become fully-qualified names
    /// and reduced extension calls become static calls (`xs.Count()` to
    /// `global::System.Linq.Enumerable.Count(xs)`), since the generated file has no
    /// using directives.
    /// </summary>
    private sealed class TypedRenderRewriter : CSharpSyntaxRewriter
    {
      private readonly SemanticModel _model;
      private readonly SyntaxNode _fragment;
      private readonly IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? _bindings;
      private readonly CancellationToken _ct;
      private readonly IReadOnlyDictionary<string, string>? _designationRenames;

      public bool Failed;

      public TypedRenderRewriter(SemanticModel model, SyntaxNode fragment, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings,
        CancellationToken ct, IReadOnlyDictionary<string, string>? designationRenames = null)
      {
        _model = model;
        _fragment = fragment;
        _bindings = bindings;
        _ct = ct;
        _designationRenames = designationRenames;
      }

      public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        => RewriteName(node, base.VisitIdentifierName(node));

      public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        => RewriteName(node, base.VisitGenericName(node));

      // Rename the declaration variable in is-pattern expressions when the analyzer has
      // assigned a temp name (e.g. `obj is User u` → `obj is User __pvtmp0_u`) to avoid
      // CS0136 with the pre-declared outer pattern variable.
      public override SyntaxNode? VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
      {
        if (_designationRenames != null
            && _designationRenames.TryGetValue(node.Identifier.ValueText, out var renamed))
        {
          return node.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(node.Identifier));
        }

        return base.VisitSingleVariableDesignation(node);
      }

      // Out-var declarations (out var x, out T x): GetSymbolInfo on the 'var' keyword
      // returns an inconsistent symbol that can cause the type to render as empty. Use
      // GetTypeInfo on the whole DeclarationExpression, which is always reliable.
      // Applies the designation rename when _designationRenames is set (condition source)
      // and keeps the original name otherwise (ExceptionStep Node lambdas).
      public override SyntaxNode? VisitDeclarationExpression(DeclarationExpressionSyntax node)
      {
        if (node.Designation is not SingleVariableDesignationSyntax desig)
        {
          return base.VisitDeclarationExpression(node);
        }

        var inferredType = _model.GetTypeInfo(node, _ct).Type;

        if (inferredType == null || !CallSiteAnalyzer.IsUsableType(inferredType, _model.Compilation))
        {
          return base.VisitDeclarationExpression(node);
        }

        var typeFqn = inferredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var outName = _designationRenames != null && _designationRenames.TryGetValue(desig.Identifier.ValueText, out var tmpName)
          ? tmpName
          : desig.Identifier.ValueText;

        return SyntaxFactory.DeclarationExpression(
          SyntaxFactory.ParseTypeName(typeFqn).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
          SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(outName)));
      }

      private SyntaxNode? RewriteName(SimpleNameSyntax original, SyntaxNode? visited)
      {
        if (IsMemberNamePosition(original))
        {
          return visited;
        }

        var symbol = _model.GetSymbolInfo(original, _ct).Symbol;

        if (symbol is INamespaceOrTypeSymbol namespaceOrType)
        {
          return SyntaxFactory.ParseName(namespaceOrType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithTriviaFrom(original);
        }

        // Bound lambda parameters (exception-step item/index/candidate) are replaced by
        // their typed replacement expression.
        if (_bindings != null
            && symbol is IParameterSymbol parameter
            && !IsDeclaredWithin(parameter, _fragment)
            && _bindings.TryGetValue(parameter.Name, out var binding)
            && binding.TypedReplacement != null)
        {
          return SyntaxFactory.ParseExpression(binding.TypedReplacement).WithTriviaFrom(original);
        }

        // Pattern variable from a prior &&-chain conjunct: replace with the pre-declared
        // outer variable (TypedReplacement is e.g. "((User)__pv0_u!)").
        if (_bindings != null
            && symbol is ILocalSymbol local
            && !IsDeclaredWithin(local, _fragment)
            && _bindings.TryGetValue(local.Name, out var localBinding)
            && localBinding.TypedReplacement != null)
        {
          return SyntaxFactory.ParseExpression(localBinding.TypedReplacement).WithTriviaFrom(original);
        }

        return visited;
      }

      public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
      {
        var symbol = _model.GetSymbolInfo(node, _ct).Symbol as IMethodSymbol;
        var visited = (InvocationExpressionSyntax?)base.VisitInvocationExpression(node);

        if (symbol is not { ReducedFrom: not null } || visited == null)
        {
          return visited;
        }

        if (visited.Expression is not MemberAccessExpressionSyntax access
            || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
          Failed = true;
          return visited;
        }

        var staticClass = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var typeArguments = access.Name is GenericNameSyntax generic ? generic.TypeArgumentList.ToString() : "";
        var arguments = new List<string> { access.Expression.ToString() };
        arguments.AddRange(visited.ArgumentList.Arguments.Select(a => a.ToString()));

        return SyntaxFactory.ParseExpression($"{staticClass}.{symbol.Name}{typeArguments}({string.Join(", ", arguments)})")
          .WithTriviaFrom(visited);
      }
    }

    /// <summary>
    /// Compiles an operand to an object-typed expression that evaluates it through the
    /// closure and runtime reflection. Supported shapes: literals, captured locals and
    /// parameters (of any type), `this` and its fields/properties/method calls (any
    /// accessibility), member access chains, parameterless LINQ Count(), and static
    /// fields/properties including enum constants on types reachable through a nameable
    /// ancestor. Anything else fails.
    /// </summary>
    private string? CompileReflective(ExpressionSyntax expression, List<(string Name, string? Type)> captures,
      IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings = null)
    {
      expression = CallSiteAnalyzer.StripParens(expression);

      switch (expression)
      {
        case LiteralExpressionSyntax literal:
          return $"(object)({literal})";

        case ThisExpressionSyntax:
          return CapturedThis;

        case AwaitExpressionSyntax awaitExpression:
        {
          // Only reachable in async render callbacks: synchronous embeddings (exception
          // steps, custom-pattern probes) exclude await fragments via ContainsAwait.
          if (CompileReflective(awaitExpression.Expression, captures, bindings) is not { } awaited)
          {
            return null;
          }

          // A nameable awaitable type gets a typed await (cast back from object): no
          // reflection, so it also survives Native AOT trimming. AwaitResult (reflective
          // Task.Result read) is the fallback for unnameable awaitable types.
          var awaitableType = _model.GetTypeInfo(awaitExpression.Expression, _ct).Type;

          if (awaitableType != null && CallSiteAnalyzer.IsUsableType(awaitableType, _compilation))
          {
            var awaitableFqn = awaitableType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"((object)(await (({awaitableFqn})({awaited}))))";
          }

          return $"(await {Runtime}.AwaitResult({awaited}))";
        }

        case IdentifierNameSyntax identifier:
          switch (_model.GetSymbolInfo(identifier, _ct).Symbol)
          {
            case ILocalSymbol { IsConst: false } local:
              // Pattern variable binding from a prior &&-chain conjunct.
              if (bindings != null && bindings.TryGetValue(local.Name, out var pvBinding))
              {
                return pvBinding.ReflectiveReplacement;
              }

              // Local declared inside the body but not bound = pattern var from another
              // conjunct without a binding → not in the closure, cannot read reflectively.
              if (IsDeclaredWithin(local, _body))
              {
                return null;
              }

              AddCapture(captures, local.Name, local.Type);
              return CallSiteAnalyzer.Identifier(local.Name);

            case IParameterSymbol param when IsDeclaredWithin(param, _body):
              return bindings != null && bindings.TryGetValue(param.Name, out var binding)
                ? binding.ReflectiveReplacement
                : null;

            case IParameterSymbol param:
              AddCapture(captures, param.Name, param.Type);
              return CallSiteAnalyzer.Identifier(param.Name);

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
              return CompileReflective(memberAccess.Expression, captures, bindings) is { } fieldReceiver
                ? $"{Runtime}.GetMemberValue({fieldReceiver}, {Quote(MetadataName(field))})"
                : null;

            case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
              return CompileReflective(memberAccess.Expression, captures, bindings) is { } propertyReceiver
                ? $"{Runtime}.GetMemberValue({propertyReceiver}, {Quote(property.Name)})"
                : null;

            default:
              return null;
          }

        case ElementAccessExpressionSyntax elementAccess when elementAccess.ArgumentList.Arguments.Count == 1:
        {
          var elementReceiver = CompileReflective(elementAccess.Expression, captures, bindings);
          var elementIndex = elementReceiver != null
            ? CompileReflective(elementAccess.ArgumentList.Arguments[0].Expression, captures, bindings)
            : null;

          return elementIndex != null
            ? $"{Runtime}.GetElementValue({elementReceiver}, {elementIndex})"
            : null;
        }

        case BinaryExpressionSyntax binary when IsReflectiveOperatorKind(binary.Kind()):
        {
          // Operators need typed operands: each side is compiled reflectively and cast
          // back to its (nameable) static type, preserving the source semantics.
          var leftType = _model.GetTypeInfo(binary.Left, _ct).Type;
          var rightType = _model.GetTypeInfo(binary.Right, _ct).Type;

          if (leftType == null || rightType == null
              || !CallSiteAnalyzer.IsUsableType(leftType, _compilation) || !CallSiteAnalyzer.IsUsableType(rightType, _compilation))
          {
            return null;
          }

          var leftCompiled = CompileReflective(binary.Left, captures, bindings);
          var rightCompiled = leftCompiled != null ? CompileReflective(binary.Right, captures, bindings) : null;

          if (rightCompiled == null)
          {
            return null;
          }

          var leftFqn = leftType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          var rightFqn = rightType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

          return $"((({leftFqn})({leftCompiled})) {binary.OperatorToken.Text} (({rightFqn})({rightCompiled})))";
        }

        case TupleExpressionSyntax tuple:
        {
          // Tuple literals are reconstructed element-wise: each element compiles
          // reflectively and is cast back to its static type, producing a real
          // ValueTuple without reflecting on the tuple itself (AOT-safe). The natural
          // type is what the compiled fragment boxes (casting to a converted type
          // would unbox-mismatch); ConvertedType only serves typeless elements
          // (null/default), where the cast is a reference conversion anyway.
          var elements = new List<string>();

          foreach (var argument in tuple.Arguments)
          {
            var elementInfo = _model.GetTypeInfo(argument.Expression, _ct);
            var elementType = elementInfo.Type ?? elementInfo.ConvertedType;

            if (elementType == null || !CallSiteAnalyzer.IsUsableType(elementType, _compilation)
                || CompileReflective(argument.Expression, captures, bindings) is not { } element)
            {
              return null;
            }

            var elementFqn = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            elements.Add($"(({elementFqn})({element}))");
          }

          return $"((object)(({string.Join(", ", elements)})))";
        }

        case InvocationExpressionSyntax invocation:
        {
          if (_model.GetSymbolInfo(invocation, _ct).Symbol is not IMethodSymbol method)
          {
            return null;
          }

          // Local functions are not members of their containing type — not resolvable
          // reflectively — but a lifted copy of the declaration can be called directly.
          if (method.MethodKind == MethodKind.LocalFunction)
          {
            return CompileLiftedLocalFunctionCall(invocation, method, captures, bindings);
          }

          // LINQ Count() works untyped: the element type never needs to be named.
          if (method is { ReducedFrom: not null, Parameters.Length: 0, Name: "Count", ContainingType.Name: "Enumerable" }
              && method.ContainingType.ContainingNamespace is { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
              && invocation.Expression is MemberAccessExpressionSyntax countAccess
              && countAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression))
          {
            return CompileReflective(countAccess.Expression, captures, bindings) is { } countReceiver
              ? $"{Runtime}.EnumerableCount({countReceiver})"
              : null;
          }

          // LINQ Count(predicate) over an unnameable element type: the predicate is
          // compiled with its parameter bound reflectively.
          if (method is { ReducedFrom: not null, Parameters.Length: 1, Name: "Count", ContainingType.Name: "Enumerable" }
              && method.ContainingType.ContainingNamespace is { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
              && CompileReflectiveStaticCall(invocation, method, captures, bindings) is null
              && invocation.Expression is MemberAccessExpressionSyntax filteredCountAccess
              && filteredCountAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
              && invocation.ArgumentList.Arguments.Count == 1
              && invocation.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax { Body: ExpressionSyntax filterBody } filterLambda
              && _model.GetDeclaredSymbol(filterLambda.Parameter, _ct) is { } filterParameter)
          {
            var filterBindings = bindings != null
              ? new Dictionary<string, CallSiteAnalyzer.LambdaBinding>(System.Linq.Enumerable.ToDictionary(bindings, kv => kv.Key, kv => kv.Value))
              : new Dictionary<string, CallSiteAnalyzer.LambdaBinding>();

            filterBindings[filterParameter.Name] = new CallSiteAnalyzer.LambdaBinding(
              CallSiteAnalyzer.IsUsableType(filterParameter.Type, _compilation)
                ? $"(({filterParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})__k)"
                : null,
              "__k");

            var countReceiver = CompileReflective(filteredCountAccess.Expression, captures, bindings);
            var countFilter = countReceiver != null ? Compile(filterBody, filterBindings) : null;

            return countFilter != null
              ? $"{Runtime}.EnumerableCount({countReceiver}, (object __k) => (bool)(object)({countFilter}))"
              : null;
          }

          if (method.IsStatic || method.ReducedFrom != null)
          {
            return CompileReflectiveStaticCall(invocation, method, captures, bindings)
                   ?? CompileReflectiveLinqCall(invocation, method, captures, bindings)
                   ?? CompileReflectiveUntypedStaticCall(invocation, method, captures, bindings);
          }

          if (method.IsGenericMethod
              || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
              || !HasSingleOverload(method))
          {
            return null;
          }

          var instanceArgExprs = GetArgumentExpressionsInParameterOrder(
            invocation.ArgumentList.Arguments, method.Parameters);
          if (instanceArgExprs == null) return null;

          var receiver = invocation.Expression switch
          {
            MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
              => CompileReflective(access.Expression, captures, bindings),
            IdentifierNameSyntax => CapturedThis, // implicit this
            _ => null,
          };

          if (receiver == null)
          {
            return null;
          }

          var arguments = new List<string>();

          foreach (var argExpr in instanceArgExprs)
          {
            if (CompileReflective(argExpr, captures, bindings) is not { } compiled)
            {
              return null;
            }

            arguments.Add(compiled);
          }

          var argumentArray = arguments.Count == 0
            ? "[]"
            : $"[{string.Join(", ", arguments)}]";

          return $"{Runtime}.InvokeInstance({receiver}, {Quote(method.Name)}, {argumentArray})";
        }

        default:
          return null;
      }
    }

    /// <summary>
    /// A static or (reduced) extension call whose full signature is nameable: emitted as
    /// a typed static call with explicit type arguments, each non-lambda argument compiled
    /// reflectively and cast to its parameter type, lambda-literal arguments pasted typed.
    /// This bridges reflective receivers (private element types upstream) into typed
    /// static calls (int.Parse, Enumerable.Where, ...).
    /// </summary>
    private string? CompileReflectiveStaticCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
      List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
    {
      var constructed = method.ReducedFrom != null ? method.GetConstructedReducedFrom() : method;

      if (constructed == null
          || !IsAccessibleMember(method)
          || !CallSiteAnalyzer.IsUsableType(method.ContainingType, _compilation)
          || constructed.TypeArguments.Any(t => !CallSiteAnalyzer.IsUsableType(t, _compilation))
          || constructed.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams))
      {
        return null;
      }

      var syntaxArguments = new List<ExpressionSyntax>();

      if (method.ReducedFrom != null)
      {
        if (invocation.Expression is not MemberAccessExpressionSyntax access
            || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
          return null;
        }

        syntaxArguments.Add(access.Expression);
      }

      // Sort named arguments to parameter positions; positional args pass through unchanged.
      // For reduced extension methods, `constructed.Parameters[0]` is the receiver (already
      // added above from access.Expression), so only match against the remaining parameters.
      var callParameters = method.ReducedFrom != null
        ? constructed.Parameters.RemoveAt(0)
        : constructed.Parameters;
      var sortedArgs = GetArgumentExpressionsInParameterOrder(
        invocation.ArgumentList.Arguments, callParameters);
      if (sortedArgs == null) return null;
      syntaxArguments.AddRange(sortedArgs);

      if (syntaxArguments.Count != constructed.Parameters.Length)
      {
        return null; // omitted optional arguments: not worth reconstructing
      }

      var rendered = new List<string>();

      for (var i = 0; i < syntaxArguments.Count; i++)
      {
        var parameterType = constructed.Parameters[i].Type;

        if (!CallSiteAnalyzer.IsUsableType(parameterType, _compilation))
        {
          return null;
        }

        var parameterTypeFqn = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (syntaxArguments[i] is AnonymousFunctionExpressionSyntax)
        {
          // Lambdas can't be object-typed; paste them typed (their parameters bind inside).
          if (CompileTyped(syntaxArguments[i], bindings) is not { } typedLambda)
          {
            return null;
          }

          rendered.Add($"({parameterTypeFqn})({typedLambda})");
          continue;
        }

        if (CompileReflective(syntaxArguments[i], captures, bindings) is not { } compiled)
        {
          return null;
        }

        rendered.Add($"(({parameterTypeFqn})({compiled}))");
      }

      var typeArguments = constructed.TypeArguments.Length > 0
        ? $"<{string.Join(", ", constructed.TypeArguments.Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))}>"
        : "";

      var containingType = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

      return $"{containingType}.{method.Name}{typeArguments}({string.Join(", ", rendered)})";
    }

    /// <summary>
    /// A local-function call inside an otherwise reflective fragment: the declaration is
    /// lifted into the generated scope and called directly, each object-typed argument
    /// cast back to its parameter type. Generic local functions stay typed-path-only.
    /// </summary>
    private string? CompileLiftedLocalFunctionCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
      List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
    {
      if (method.IsGenericMethod
          || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
          || invocation.ArgumentList.Arguments.Count != method.Parameters.Length
          || invocation.ArgumentList.Arguments.Any(a => a.NameColon != null)
          || method.Parameters.Any(p => !CallSiteAnalyzer.IsUsableType(p.Type, _compilation))
          || !TryLiftLocalFunction(method))
      {
        return null;
      }

      var rendered = new List<string>();

      for (var i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
      {
        if (CompileReflective(invocation.ArgumentList.Arguments[i].Expression, captures, bindings) is not { } compiled)
        {
          return null;
        }

        var parameterTypeFqn = method.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        rendered.Add($"(({parameterTypeFqn})({compiled}))");
      }

      return $"{CallSiteAnalyzer.Identifier(method.Name)}({string.Join(", ", rendered)})";
    }

    /// <summary>
    /// A static call the typed strategies rejected (private helper methods, unnameable
    /// parameter types): the containing type is anchored via TypeAccessor and the method
    /// resolved by name + argument count at runtime — the static counterpart of the
    /// InvokeInstance path for private instance members. Reduced extension calls and
    /// generic methods stay out (type arguments cannot be reconstructed reflectively).
    /// </summary>
    private string? CompileReflectiveUntypedStaticCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
      List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
    {
      if (method.ReducedFrom != null
          || method.IsGenericMethod
          || method.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams)
          || !HasSingleStaticOverload(method)
          || TypeAccessor(method.ContainingType) is not { } typeAccessor)
      {
        return null;
      }

      var untypedArgExprs = GetArgumentExpressionsInParameterOrder(
        invocation.ArgumentList.Arguments, method.Parameters);
      if (untypedArgExprs == null) return null;

      var arguments = new List<string>();

      foreach (var argExpr in untypedArgExprs)
      {
        if (CompileReflective(argExpr, captures, bindings) is not { } compiled)
        {
          return null;
        }

        arguments.Add(compiled);
      }

      var argumentArray = arguments.Count == 0
        ? "[]"
        : $"[{string.Join(", ", arguments)}]";

      return $"{Runtime}.InvokeStatic({typeAccessor}, {Quote(method.Name)}, {argumentArray})";
    }

    /// <summary>
    /// Returns argument expressions sorted to parameter order, resolving named arguments.
    /// Returns null if any named label doesn't match a parameter or if there's a conflict.
    /// </summary>
    private static List<ExpressionSyntax>? GetArgumentExpressionsInParameterOrder(
      SeparatedSyntaxList<ArgumentSyntax> args, System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters)
    {
      if (args.Count != parameters.Length) return null;

      if (!args.Any(a => a.NameColon != null))
        return args.Select(a => a.Expression).ToList();

      var result = new ExpressionSyntax?[parameters.Length];

      for (var i = 0; i < args.Count; i++)
      {
        var arg = args[i];

        if (arg.NameColon != null)
        {
          var label = arg.NameColon.Name.Identifier.ValueText;
          var paramIdx = -1;
          for (var j = 0; j < parameters.Length; j++)
            if (parameters[j].Name == label) { paramIdx = j; break; }
          if (paramIdx < 0 || result[paramIdx] != null) return null;
          result[paramIdx] = arg.Expression;
        }
        else
        {
          if (result[i] != null) return null;
          result[i] = arg.Expression;
        }
      }

      return result.All(r => r != null) ? result.Select(r => r!).ToList() : null;
    }

    /// <summary>
    /// Runtime resolution is by name + parameter count: the combination must be
    /// unambiguous across the hierarchy InvokeStatic searches.
    /// </summary>
    private static bool HasSingleStaticOverload(IMethodSymbol method)
    {
      var count = 0;

      for (var type = method.ContainingType; type != null; type = type.BaseType)
      {
        foreach (var member in type.GetMembers(method.Name))
        {
          if (member is IMethodSymbol { IsStatic: true } candidate
              && candidate.Parameters.Length == method.Parameters.Length)
          {
            count++;
          }
        }
      }

      return count == 1;
    }

    /// <summary>
    /// Parameterless LINQ extension calls (First, Single, ...) over collections whose
    /// element type cannot be named: resolved at runtime via reflection.
    /// </summary>
    private string? CompileReflectiveLinqCall(InvocationExpressionSyntax invocation, IMethodSymbol method,
      List<(string Name, string? Type)> captures, IReadOnlyDictionary<string, CallSiteAnalyzer.LambdaBinding>? bindings)
    {
      if (method is not { ReducedFrom: not null, Parameters.Length: 0, ContainingType.Name: "Enumerable" }
          || method.ContainingType.ContainingNamespace is not { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
          || invocation.Expression is not MemberAccessExpressionSyntax access
          || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
      {
        return null;
      }

      return CompileReflective(access.Expression, captures, bindings) is { } source
        ? $"{Runtime}.InvokeLinq({source}, {Quote(method.Name)})"
        : null;
    }

    /// <summary>
    /// A typeof()/GetNestedType() chain for a (possibly unnameable) type: the outermost
    /// nameable ancestor anchors a typeof(), and each unnameable nesting level is resolved
    /// by metadata name at runtime. Null when no nameable anchor exists.
    /// </summary>
    public string? TypeAccessor(ITypeSymbol type)
    {
      if (CallSiteAnalyzer.IsUsableType(type, _compilation))
      {
        return $"typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
      }

      if (type is not INamedTypeSymbol { IsGenericType: false, IsFileLocal: false, ContainingType: { } parent })
      {
        return null;
      }

      return TypeAccessor(parent) is { } parentAccessor
        ? $"{Runtime}.GetNestedType({parentAccessor}, {Quote(type.MetadataName)})"
        : null;
    }

    private string? StaticMember(INamedTypeSymbol containingType, string memberName)
    {
      return TypeAccessor(containingType) is { } typeAccessor
        ? $"{Runtime}.GetStaticMemberValue({typeAccessor}, {Quote(memberName)})"
        : null;
    }

    private static bool IsReflectiveOperatorKind(SyntaxKind kind)
    {
      return kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
        or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
        or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression
        or SyntaxKind.AddExpression or SyntaxKind.SubtractExpression
        or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression;
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
      var typeFqn = CallSiteAnalyzer.IsUsableType(type, _compilation)
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
  }
}
