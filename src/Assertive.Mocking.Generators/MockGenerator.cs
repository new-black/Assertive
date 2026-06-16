using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Assertive.Mocking.Generators
{
  /// <summary>
  /// Generates a concrete, recording implementation for every interface used in an
  /// <c>A&lt;T&gt;()</c> or <c>A&lt;T&gt;(arrange)</c> call, plus a module initializer that
  /// registers each one with MockFactoryRegistry. No runtime proxy generation: the implementation
  /// is plain C# emitted at compile time, so generated mocks are trimming- and Native-AOT-safe.
  ///
  /// Arrangement itself is NOT generated: A&lt;T&gt; executes its lambda at runtime (mirroring how
  /// Assertive executes assertion delegates), so ordinary setup code behaves as written. The
  /// generator only needs to know which interfaces to materialize.
  /// </summary>
  [Generator]
  public sealed class MockGenerator : IIncrementalGenerator
  {
    private static readonly DiagnosticDescriptor Mock001 = new DiagnosticDescriptor(
      id: "MOCK001",
      title: "Cannot mock this type",
      messageFormat: "Cannot mock '{0}': {1}",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Only non-sealed, non-static classes and interfaces can be mocked.");

    private static readonly DiagnosticDescriptor Mock002 = new DiagnosticDescriptor(
      id: "MOCK002",
      title: "Arranging a non-virtual member has no effect",
      messageFormat: "'{0}.{1}' is not virtual or abstract; this arrangement will have no effect (the real implementation always runs on class mocks)",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Only virtual or abstract members can be intercepted on class mocks.");

    private static readonly DiagnosticDescriptor Mock003 = new DiagnosticDescriptor(
      id: "MOCK003",
      title: "Member cannot be mocked",
      messageFormat: "'{0}.{1}' cannot be mocked: {2}. Arrangements on it will be silently ignored.",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Mock004 = new DiagnosticDescriptor(
      id: "MOCK004",
      title: "Named arguments not supported in matcher calls",
      messageFormat: "Named arguments at mock call sites are not supported; matchers (default, It.Any) will be treated as exact values. Use positional arguments instead.",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      var arranged = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsGenericCallNamed(node, "A"),
          transform: static (ctx, ct) => ExtractTarget(ctx, ct, "A", "Mock"))
        .Where(static t => !t.IsDefaultOrEmpty);

      var matcherCalls = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsPotentialMatcherCall(node),
          transform: static (ctx, ct) => ExtractMatcherCall(ctx, ct))
        .Where(static c => c is not null);

      // MOCK001: emit a diagnostic when a non-mockable type is used.
      var arrangedDiagnostics = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsGenericCallNamed(node, "A"),
          transform: static (ctx, ct) => ExtractDiagnostic(ctx, ct, "A", "Mock"))
        .Where(static d => d is not null);

      context.RegisterSourceOutput(
        arrangedDiagnostics.Collect(),
        static (spc, diags) =>
        {
          foreach (var d in diags) if (d is not null) spc.ReportDiagnostic(d);
        });

      var buildCalls = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsGenericCallNamed(node, "Build"),
          transform: static (ctx, ct) => ExtractBuildCall(ctx, ct))
        .Where(static c => c is not null);

      var wrapTargets = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsGenericCallNamed(node, "Wrap"),
          transform: static (ctx, ct) => ExtractWrapTarget(ctx, ct))
        .Where(static w => w is not null);

      context.RegisterSourceOutput(
        arranged.Collect().Combine(matcherCalls.Collect()).Combine(buildCalls.Collect()).Combine(wrapTargets.Collect()),
        static (spc, data) => Emit(spc, data.Left.Left.Left, data.Left.Left.Right, data.Left.Right, data.Right));

      // MOCK002: warn when a non-virtual class member is arranged (the real impl always runs).
      var mock002Diagnostics = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax },
          transform: static (ctx, ct) => ExtractMock002(ctx, ct))
        .Where(static d => d is not null);

      context.RegisterSourceOutput(mock002Diagnostics.Collect(), static (spc, diags) =>
      {
        foreach (var d in diags) if (d is not null) spc.ReportDiagnostic(d);
      });

      // MOCK003: warn when a generic or ref/out member is arranged inside an arrange context.
      var mock003Diagnostics = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax },
          transform: static (ctx, ct) => ExtractMock003(ctx, ct))
        .Where(static d => d is not null);

      context.RegisterSourceOutput(mock003Diagnostics.Collect(), static (spc, diags) =>
      {
        foreach (var d in diags) if (d is not null) spc.ReportDiagnostic(d);
      });

      // MOCK004: warn when named arguments are used at a matcher call site inside an arrange context.
      var mock004Diagnostics = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsPotentialMatcherCall(node),
          transform: static (ctx, ct) => ExtractMock004(ctx, ct))
        .Where(static d => d is not null);

      context.RegisterSourceOutput(mock004Diagnostics.Collect(), static (spc, diags) =>
      {
        foreach (var d in diags) if (d is not null) spc.ReportDiagnostic(d);
      });

    }

    private static Diagnostic? ExtractDiagnostic(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct, string expectedName, string expectedType)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ct);

      IMethodSymbol? method = symbolInfo.Symbol as IMethodSymbol;
      if (method is null)
      {
        foreach (var candidate in symbolInfo.CandidateSymbols)
        {
          if (candidate is IMethodSymbol m) { method = m; break; }
        }
      }

      if (method is null) return null;
      if (method.Name != expectedName || method.ContainingType?.Name != expectedType) return null;
      if (method.ContainingType.ContainingNamespace is not { Name: "Mocking", ContainingNamespace.Name: "Assertive" }) return null;
      if (method.TypeArguments.Length != 1 || method.TypeArguments[0] is not INamedTypeSymbol target) return null;

      // Only report for types that are clearly not mockable (don't generate a mock for them either).
      if (IsMockableType(target)) return null;

      string reason;
      if (target.TypeKind == TypeKind.Struct || target.TypeKind == TypeKind.Enum)
        reason = "it is a struct/value type";
      else if (target.IsStatic)
        reason = "it is static";
      else if (target.IsSealed)
        reason = "it is sealed";
      else if (target.IsGenericType && target.TypeArguments.Any(HasOpenTypeArgument))
        reason = "it is an open generic type (only closed generic interfaces are supported)";
      else
        reason = "it cannot be mocked (only non-sealed classes and interfaces are supported)";

      return Diagnostic.Create(Mock001, invocation.GetLocation(),
        target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        reason);
    }

    /// <summary>
    /// MOCK002: warns when an arrangement lambda calls a non-virtual, non-abstract method on a
    /// class mock — the real implementation will always run and the arrangement has no effect.
    /// </summary>
    private static Diagnostic? ExtractMock002(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      // Only care about calls inside A<T>(...) / When(...) / Received(...) arrange lambdas.
      if (!IsInArrangeContext(invocation))
      {
        return null;
      }

      // Resolve the method being called.
      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
      {
        return null;
      }

      var containingType = method.ContainingType;

      // Only class receivers; interfaces are always interceptable.
      if (containingType is null || containingType.TypeKind != TypeKind.Class)
      {
        return null;
      }

      // If the class isn't mockable, MOCK001 already covers it — skip.
      if (!IsMockableType(containingType))
      {
        return null;
      }

      // Virtual, abstract, and override members ARE interceptable; warn only for concrete non-virtual ones.
      if (method.IsVirtual || method.IsAbstract || method.IsOverride)
      {
        return null;
      }

      return Diagnostic.Create(
        Mock002,
        invocation.GetLocation(),
        containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        method.Name);
    }

    /// <summary>
    /// MOCK003: warns when a generic method or a method with ref/out parameters is arranged. The
    /// generator intentionally skips such members so an arrangement on them silently does nothing.
    /// Only fires for methods on interface or non-sealed class types (the kinds that get mocked),
    /// to avoid false positives on DSL/framework helpers (It.Any, ArrangeExtensions.Returns, etc.).
    /// </summary>
    private static Diagnostic? ExtractMock003(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      if (!IsInArrangeContext(invocation))
      {
        return null;
      }

      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
      {
        return null;
      }

      var containingType = method.ContainingType;
      if (containingType is null)
      {
        return null;
      }

      // Only warn for methods on mockable types (interface or non-sealed, non-static class that is
      // not part of the Assertive.Mocking framework itself).
      if (!IsMockableType(containingType))
      {
        return null;
      }

      // Skip Assertive.Mocking framework types — they live in the Assertive namespace.
      for (var ns = containingType.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
      {
        if (ns is { Name: "Assertive", ContainingNamespace.IsGlobalNamespace: true })
        {
          return null;
        }
      }

      string? reason = null;

      if (method.IsGenericMethod)
      {
        reason = "it is a generic method";
      }
      else if (method.ReturnsByRef)
      {
        reason = "it returns by ref";
      }
      else if (method.Parameters.Any(p => p.RefKind != RefKind.None))
      {
        reason = "it has ref/out parameters";
      }

      if (reason is null)
      {
        return null;
      }

      return Diagnostic.Create(
        Mock003,
        invocation.GetLocation(),
        containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        method.Name,
        reason);
    }

    /// <summary>
    /// MOCK004: warns when named arguments are used at a matcher call site inside an arrange
    /// context. Named args desync the positional matcher queue, so they are not supported.
    /// </summary>
    private static Diagnostic? ExtractMock004(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      if (!IsInArrangeContext(invocation))
      {
        return null;
      }

      var args = invocation.ArgumentList.Arguments;

      if (!args.Any(a => a.NameColon != null))
      {
        return null;
      }

      // Only fire when the call has at least one matcher argument (pre-filter already checked this
      // but double-check to avoid false positives on unrelated calls with named args).
      if (!args.Any(a => UnwrapNullForgiving(a.Expression).IsKind(SyntaxKind.DefaultLiteralExpression) || IsMatcherCall(UnwrapNullForgiving(a.Expression))))
      {
        return null;
      }

      return Diagnostic.Create(Mock004, invocation.GetLocation());
    }

    private static string BuildConstraintClauses(System.Collections.Immutable.ImmutableArray<ITypeParameterSymbol> typeParams)
    {
      var sb = new System.Text.StringBuilder();
      foreach (var tp in typeParams)
      {
        var constraints = new List<string>();
        if (tp.HasReferenceTypeConstraint) constraints.Add("class");
        if (tp.HasValueTypeConstraint) constraints.Add("struct");
        if (tp.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
        if (tp.HasNotNullConstraint) constraints.Add("notnull");
        foreach (var ct in tp.ConstraintTypes)
          constraints.Add(ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (tp.HasConstructorConstraint) constraints.Add("new()");
        if (constraints.Count > 0)
          sb.Append($" where {tp.Name} : {string.Join(", ", constraints)}");
      }
      return sb.ToString();
    }

    private static bool IsMatcherMethodName(string name) =>
      name is "Any" or "IsNotNull" or "IsIn" or "IsInRange" or "Contains" or "IsEmpty";

    /// <summary>Cheap pre-filter: a member call with at least one matcher-looking argument (bare default or a matcher invocation).</summary>
    private static bool IsPotentialMatcherCall(SyntaxNode node)
    {
      return node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax } inv
        && inv.ArgumentList.Arguments.Any(static a =>
          UnwrapNullForgiving(a.Expression).IsKind(SyntaxKind.DefaultLiteralExpression)
          || IsMatcherCall(UnwrapNullForgiving(a.Expression)));
    }

    /// <summary>Strips a null-forgiving operator (<c>!</c>) if present, returning the inner expression.</summary>
    private static ExpressionSyntax UnwrapNullForgiving(ExpressionSyntax expr) =>
      expr is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } pue
        ? pue.Operand
        : expr;

    /// <summary>Returns true for any matcher helper invocation: unqualified (<c>Any&lt;T&gt;()</c>, <c>IsNotNull&lt;T&gt;()</c>, …) from <c>using static Mock</c>.</summary>
    private static bool IsMatcherCall(ExpressionSyntax expr)
    {
      if (expr is not InvocationExpressionSyntax call) return false;

      var name = call.Expression switch
      {
        GenericNameSyntax g => g.Identifier.ValueText,
        IdentifierNameSyntax i => i.Identifier.ValueText,
        _ => null,
      };
      return name != null && IsMatcherMethodName(name);
    }

    private static MatcherCall? ExtractMatcherCall(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      // Only intercept calls lexically inside an A / When / Received arrangement lambda.
      if (!IsInArrangeContext(invocation))
      {
        return null;
      }

      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
          || method.ContainingType is not { TypeKind: TypeKind.Interface }
          || method.IsGenericMethod
          || method.Parameters.Any(p => p.RefKind != RefKind.None))
      {
        return null;
      }

      var args = invocation.ArgumentList.Arguments;
      var kinds = args.Select(a => Classify(a.Expression)).ToImmutableArray();

      // Nothing to do unless at least one argument is a matcher.
      if (!kinds.Any(k => k != ArgKind.Exact))
      {
        return null;
      }

      var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);

      if (location is null)
      {
        return null;
      }

      return new MatcherCall(
        method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        method.Name,
        method.ReturnsVoid ? null : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToImmutableArray(),
        kinds,
        location.Version,
        location.Data,
        location.GetDisplayLocation());
    }

    private static ArgKind Classify(ExpressionSyntax expression)
    {
      expression = UnwrapNullForgiving(expression);

      // Bare `default` (not default(T)) is the "any" token.
      if (expression.IsKind(SyntaxKind.DefaultLiteralExpression))
      {
        return ArgKind.Any;
      }

      // Unqualified matcher call from `using static Mock`: Any<T>(), IsNotNull<T>(), IsIn<T>(...), etc.
      // Only matches plain GenericNameSyntax / IdentifierNameSyntax (not member-access expressions like
      // list.Contains(x)) to avoid false positives.
      if (expression is InvocationExpressionSyntax call)
      {
        var name = call.Expression switch
        {
          GenericNameSyntax g => g.Identifier.ValueText,
          IdentifierNameSyntax i => i.Identifier.ValueText,
          _ => null,
        };

        if (name != null && IsMatcherMethodName(name))
        {
          return name == "Any" && call.ArgumentList.Arguments.Count == 0 ? ArgKind.Any : ArgKind.Predicate;
        }
      }

      return ArgKind.Exact;
    }

    private static bool IsInArrangeContext(SyntaxNode node)
    {
      for (var current = node.Parent; current != null; current = current.Parent)
      {
        // Inside an A<T>(...) / When(...) / Received(...) / Setup(...) lambda.
        if (current is InvocationExpressionSyntax invocation && CalleeName(invocation) is "A" or "When" or "Received" or "Setup")
        {
          return true;
        }

        // Standalone arrange: mock.Method(Any<T>()).Returns(v) — the mock call (or an argument
        // inside it) is the receiver of a trailing arrange verb.
        if (current is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Returns" or "Throws" or "Does" or "ReturnsMany" })
        {
          return true;
        }
      }

      return false;
    }

    private static string? CalleeName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
      MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
      GenericNameSyntax g => g.Identifier.ValueText,
      IdentifierNameSyntax i => i.Identifier.ValueText,
      _ => null,
    };

    private static bool IsGenericCallNamed(SyntaxNode node, string name)
    {
      if (node is not InvocationExpressionSyntax invocation)
      {
        return false;
      }

      return invocation.Expression switch
      {
        MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g.Identifier.ValueText == name && g.TypeArgumentList.Arguments.Count == 1,
        GenericNameSyntax g => g.Identifier.ValueText == name && g.TypeArgumentList.Arguments.Count == 1,
        _ => false,
      };
    }

    private static ImmutableArray<MockTarget> ExtractTarget(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct, string expectedName, string expectedType)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ct);

      // When the lambda body has errors (e.g. an unresolved Any() overload), the outer call may
      // land in CandidateSymbols instead of Symbol. Accept the first matching candidate so that
      // the generator still emits the mock class and the MockArrange overloads it needs.
      IMethodSymbol? method = symbolInfo.Symbol as IMethodSymbol;
      if (method is null)
      {
        foreach (var candidate in symbolInfo.CandidateSymbols)
        {
          if (candidate is IMethodSymbol m)
          {
            method = m;
            break;
          }
        }
      }

      if (method is null)
      {
        return ImmutableArray<MockTarget>.Empty;
      }

      if (method.Name != expectedName || method.ContainingType?.Name != expectedType)
      {
        return ImmutableArray<MockTarget>.Empty;
      }

      if (method.ContainingType.ContainingNamespace is not { Name: "Mocking", ContainingNamespace.Name: "Assertive" })
      {
        return ImmutableArray<MockTarget>.Empty;
      }

      if (method.TypeArguments.Length != 1 || method.TypeArguments[0] is not INamedTypeSymbol target || !IsMockableType(target))
      {
        return ImmutableArray<MockTarget>.Empty;
      }

      return BuildClosure(target);
    }

    /// <summary>A type we can mock at the top level: a non-generic or closed-generic interface, or a non-sealed/non-static class.</summary>
    private static bool IsMockableType(INamedTypeSymbol type)
    {
      // Open generics (any unbound type parameter anywhere in the argument tree) cannot be mocked.
      if (type.IsGenericType && type.TypeArguments.Any(HasOpenTypeArgument))
      {
        return false;
      }

      // Abstract classes are fine to mock; sealed/static are not subclassable.
      return type.TypeKind == TypeKind.Interface
        || (type.TypeKind == TypeKind.Class && !type.IsSealed && !type.IsStatic);
    }

    /// <summary>
    /// Builds a mock target for <paramref name="root"/> and, transitively, for every interface
    /// reachable through a method's return type (recursive auto-mocking). The closure over types
    /// is finite even when the call graph is infinitely deep, so unbounded-depth auto-mocking
    /// needs only finitely many generated classes. Cycles terminate via the visited set.
    /// </summary>
    private static ImmutableArray<MockTarget> BuildClosure(INamedTypeSymbol root)
    {
      var visited = new Dictionary<string, MockTarget>();
      var queue = new Queue<INamedTypeSymbol>();

      queue.Enqueue(root);

      while (queue.Count > 0)
      {
        var iface = queue.Dequeue();
        var fqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (visited.ContainsKey(fqn))
        {
          continue;
        }

        var target = BuildTarget(iface, out var children);
        visited[fqn] = target;

        foreach (var child in children)
        {
          if (!visited.ContainsKey(child.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
          {
            queue.Enqueue(child);
          }
        }
      }

      return visited.Values.ToImmutableArray();
    }

    private static MockTarget BuildTarget(INamedTypeSymbol type, out List<INamedTypeSymbol> children)
    {
      children = new List<INamedTypeSymbol>();

      var isClass = type.TypeKind == TypeKind.Class;

      // Interface: every member (including inherited interface members). Class: only overridable
      // members declared on the class hierarchy (excluding System.Object's), since a subclass can
      // intercept only virtual/abstract members.
      //
      // Process the primary type first so its members take precedence. Base interface members with
      // the same (name, paramTypes) signature but a different return type — which can happen with
      // closed generic interfaces that redefine a method from a non-generic base — are emitted as
      // explicit interface implementations rather than trackable mock methods.
      var members = isClass
        ? ClassOverridableMembers(type)
        : new[] { type }.Concat(type.AllInterfaces).SelectMany(i => i.GetMembers());

      var methods = new List<MockMethod>();
      var properties = new List<MockProperty>();
      var indexers = new List<MockIndexer>();
      var events = new List<MockEvent>();
      var genericStubs = new List<string>(); // NotImplementedException stubs for generic interface methods
      // Tracks (methodName|param0Type|param1Type|...) keys already claimed by a mock method.
      // Used to detect return-type conflicts from base interfaces and emit explicit stubs instead.
      var seenMethodKeys = new HashSet<string>();

      foreach (var member in members)
      {
        if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } m && !m.IsGenericMethod
            && !m.ReturnsByRef && m.Parameters.All(p => p.RefKind == RefKind.None))
        {
          var methodKey = m.Name + "|" + string.Join("|", m.Parameters.Select(p =>
            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

          if (!seenMethodKeys.Add(methodKey))
          {
            // A method with the same (name, paramTypes) was already emitted from the primary interface.
            // This base-interface method has a different return type; emit it as an explicit interface
            // implementation stub so the class still satisfies the base interface contract.
            var returnFqn = m.ReturnsVoid ? "void" : m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var paramList = string.Join(", ", m.Parameters.Select(p =>
              $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}"));
            var containingFqn = m.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var body = m.ReturnsVoid ? "{ }" : "=> default!;";
            genericStubs.Add($"    {returnFqn} {containingFqn}.{m.Name}({paramList}) {body}");
          }
          else
          {
            var (kind, innerFqn, innerMock, directMock, elementFqn) = AnalyzeReturn(m, children);

            // Class virtual (non-abstract) members run the real base when unarranged; abstracts and
            // interface members have no base, so they fall back to default/auto-mock.
            var callBase = isClass && !m.IsAbstract;

            methods.Add(new MockMethod(
              m.Name,
              m.ReturnsVoid ? null : m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
              m.Parameters.Select(p => (p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), p.Name)).ToArray(),
              kind, innerFqn, innerMock, directMock, callBase, elementFqn));
          }
        }
        else if (!isClass && member is IMethodSymbol { MethodKind: MethodKind.Ordinary } sm
                 && (sm.IsGenericMethod || sm.ReturnsByRef || sm.Parameters.Any(p => p.RefKind != RefKind.None)))
        {
          // Interface methods that can't be intercepted (generic, ref-return, or ref/out params) must still be implemented.
          var typeParamSuffix = sm.IsGenericMethod ? $"<{string.Join(", ", sm.TypeParameters.Select(tp => tp.Name))}>" : "";
          var constraintClauses = sm.IsGenericMethod ? BuildConstraintClauses(sm.TypeParameters) : "";
          var returnTypeFqn = sm.ReturnsVoid ? "void" : sm.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          var paramList = string.Join(", ", sm.Parameters.Select(p =>
          {
            var modifier = p.RefKind switch { RefKind.Out => "out ", RefKind.Ref => "ref ", _ => "" };
            return $"{modifier}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}";
          }));
          // Assign out params before throwing to satisfy definite-assignment; throw keeps it simple.
          var outAssignments = sm.Parameters.Where(p => p.RefKind == RefKind.Out)
            .Select(p => $" {p.Name} = default!;");
          var body = outAssignments.Any()
            ? $"{{ {string.Concat(outAssignments)} return default!; }}"
            : (sm.ReturnsVoid ? "{ }" : "=> default!;");
          genericStubs.Add($"    public {returnTypeFqn} {sm.Name}{typeParamSuffix}({paramList}){constraintClauses} {body}");
        }
        else if (member is IPropertySymbol { IsIndexer: false } p)
        {
          var propCallBase = isClass && !p.IsAbstract;
          var hasSetter = p.SetMethod is not null
            && p.SetMethod.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
          properties.Add(new MockProperty(p.Name, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), propCallBase, hasSetter));
        }
        else if (member is IPropertySymbol { IsIndexer: true } idx)
        {
          var idxCallBase = isClass && !idx.IsAbstract;
          indexers.Add(new MockIndexer(
            idx.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            idx.Parameters.Select(pp => (pp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), pp.Name)).ToArray(),
            idx.GetMethod is not null,
            idx.SetMethod is not null,
            idxCallBase));
        }
        else if (member is IEventSymbol e)
        {
          var handlerFqn = e.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          events.Add(new MockEvent(e.Name, handlerFqn));
        }
      }

      var constructors = isClass
        ? type.InstanceConstructors
          .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
          .Select(c => c.Parameters.Select(pp => pp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToImmutableArray())
          .ToImmutableArray()
        : ImmutableArray<ImmutableArray<string>>.Empty;

      return new MockTarget(
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        SanitizedName(type),
        type.Name,
        isClass,
        methods.ToImmutableArray(),
        properties.ToImmutableArray(),
        constructors,
        indexers.ToImmutableArray(),
        events.ToImmutableArray(),
        genericStubs.ToImmutableArray());
    }

    /// <summary>Overridable methods and properties across a class's hierarchy: virtual/abstract, not sealed, public, excluding System.Object's.</summary>
    private static IEnumerable<ISymbol> ClassOverridableMembers(INamedTypeSymbol type)
    {
      var seen = new HashSet<string>();

      for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
      {
        foreach (var member in current.GetMembers())
        {
          if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } m)
          {
            if (!(m.IsVirtual || m.IsAbstract) || m.IsSealed || m.DeclaredAccessibility != Accessibility.Public)
              continue;
            if (seen.Add($"{m.Name}`{m.Parameters.Length}"))
              yield return m;
          }
          else if (member is IPropertySymbol { IsIndexer: false } p)
          {
            if (!(p.IsVirtual || p.IsAbstract) || p.IsSealed || p.DeclaredAccessibility != Accessibility.Public)
              continue;
            if (seen.Add($"prop:{p.Name}"))
              yield return p;
          }
          else if (member is IPropertySymbol { IsIndexer: true } idx)
          {
            if (!(idx.IsVirtual || idx.IsAbstract) || idx.IsSealed || idx.DeclaredAccessibility != Accessibility.Public)
              continue;
            var idxKey = $"indexer:{string.Join(",", idx.Parameters.Select(pp => pp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))}";
            if (seen.Add(idxKey))
              yield return idx;
          }
          else if (member is IEventSymbol e)
          {
            if (!(e.IsVirtual || e.IsAbstract) || e.IsSealed || e.DeclaredAccessibility != Accessibility.Public)
              continue;
            if (seen.Add($"event:{e.Name}"))
              yield return e;
          }
        }
      }
    }

    /// <summary>
    /// Classifies a method's return type for default-value generation, adding any auto-mockable
    /// interface (direct or inside Task&lt;T&gt;/ValueTask&lt;T&gt;) to <paramref name="children"/>.
    /// Returns (Kind, InnerFqn, InnerMock, DirectMock, ElementTypeFqn).
    /// </summary>
    private static (ReturnKind Kind, string? InnerFqn, string? InnerMock, string? DirectMock, string? ElementTypeFqn) AnalyzeReturn(IMethodSymbol method, List<INamedTypeSymbol> children)
    {
      if (method.ReturnsVoid)
      {
        return (ReturnKind.Void, null, null, null, null);
      }

      // Array type: T[] is an IArrayTypeSymbol, not INamedTypeSymbol.
      if (method.ReturnType is IArrayTypeSymbol arrayType)
      {
        var elementFqn = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return (ReturnKind.EmptyCollection, null, null, null, elementFqn);
      }

      if (method.ReturnType is not INamedTypeSymbol named)
      {
        return (ReturnKind.Value, null, null, null, null);
      }

      var taskKind = TaskShape(named);

      if (taskKind != null)
      {
        if (!named.IsGenericType)
        {
          return (taskKind.Value == ReturnKind.TaskOfT ? ReturnKind.Task : ReturnKind.ValueTask, null, null, null, null);
        }

        var inner = named.TypeArguments[0];
        var innerFqn = inner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string? innerMock = null;

        if (inner is INamedTypeSymbol innerNamed && IsAutoMockable(innerNamed))
        {
          innerMock = SanitizedName(innerNamed);
          children.Add(innerNamed);
        }

        return (taskKind.Value, innerFqn, innerMock, null, null);
      }

      // Direct interface return.
      if (IsAutoMockable(named))
      {
        children.Add(named);
        return (ReturnKind.AutoMock, null, null, SanitizedName(named), null);
      }

      // Collection types: IEnumerable<T>, ICollection<T>, IList<T>, List<T>, IReadOnlyList<T>, IReadOnlyCollection<T>.
      var collectionElementFqn = GetCollectionElementFqn(named);
      if (collectionElementFqn != null)
      {
        return (ReturnKind.EmptyCollection, null, null, null, collectionElementFqn);
      }

      return (ReturnKind.Value, null, null, null, null);
    }

    /// <summary>
    /// If <paramref name="type"/> is a well-known generic collection type (IEnumerable&lt;T&gt;,
    /// IList&lt;T&gt;, List&lt;T&gt;, ICollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, IReadOnlyCollection&lt;T&gt;),
    /// returns the FQN of the element type T; otherwise null.
    /// </summary>
    private static string? GetCollectionElementFqn(INamedTypeSymbol type)
    {
      if (!type.IsGenericType || type.TypeArguments.Length != 1)
      {
        return null;
      }

      // Check the type itself or any of its interfaces for IEnumerable<T>.
      // We accept: IEnumerable<T>, ICollection<T>, IList<T>, List<T>, IReadOnlyList<T>, IReadOnlyCollection<T>.
      var name = type.Name;
      var isKnownCollection =
        (name is "IEnumerable" or "ICollection" or "IList" or "IReadOnlyList" or "IReadOnlyCollection"
         && IsInNamespace(type.ContainingNamespace, "System", "Collections", "Generic"))
        || (name == "List" && IsInNamespace(type.ContainingNamespace, "System", "Collections", "Generic"));

      if (!isKnownCollection)
      {
        return null;
      }

      return type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>Returns TaskOfT for Task/Task&lt;T&gt;, ValueTaskOfT for ValueTask/ValueTask&lt;T&gt;, else null.</summary>
    private static ReturnKind? TaskShape(INamedTypeSymbol type)
    {
      if (!IsInNamespace(type.ContainingNamespace, "System", "Threading", "Tasks"))
      {
        return null;
      }

      return type.Name switch
      {
        "Task" => ReturnKind.TaskOfT,
        "ValueTask" => ReturnKind.ValueTaskOfT,
        _ => null,
      };
    }

    private static bool IsInNamespace(INamespaceSymbol? ns, params string[] parts)
    {
      for (var i = parts.Length - 1; i >= 0; i--)
      {
        if (ns is null || ns.Name != parts[i])
        {
          return false;
        }

        ns = ns.ContainingNamespace;
      }

      return ns is { IsGlobalNamespace: true };
    }

    /// <summary>
    /// A return type we can recursively auto-mock: a public, non-open-generic type outside System.* —
    /// any interface (including closed generic interfaces), or a non-sealed class with an accessible
    /// parameterless constructor (auto-mock can't supply constructor arguments).
    /// </summary>
    private static bool IsAutoMockable(INamedTypeSymbol type)
    {
      if (type.DeclaredAccessibility != Accessibility.Public)
      {
        return false;
      }

      // Only closed generic interfaces can be auto-mocked. Generic classes are excluded because
      // they are typically data types (potentially with required members) rather than service interfaces.
      if (type.IsGenericType && (type.TypeKind != TypeKind.Interface || type.TypeArguments.Any(HasOpenTypeArgument)))
      {
        return false;
      }

      var kindOk = type.TypeKind == TypeKind.Interface
        || (type.TypeKind == TypeKind.Class && !type.IsSealed && !type.IsStatic && !type.IsAbstract
            && type.InstanceConstructors.Any(c => c.Parameters.Length == 0
                && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal));

      if (!kindOk)
      {
        return false;
      }

      // Avoid auto-mocking framework interfaces (e.g. IEnumerable, IDisposable) — those want
      // default/empty, not a mock.
      for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
      {
        if (ns is { Name: "System", ContainingNamespace.IsGlobalNamespace: true })
        {
          return false;
        }
      }

      return true;
    }

    private static bool HasOpenTypeArgument(ITypeSymbol t) =>
      t.Kind == SymbolKind.TypeParameter ||
      (t is INamedTypeSymbol named && named.TypeArguments.Any(HasOpenTypeArgument));

    private static string SanitizedName(INamedTypeSymbol iface)
    {
      var full = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        .Replace("global::", "")
        .Replace('.', '_');

      return "Mock_" + new string(full.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    private static WrapTarget? ExtractWrapTarget(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ct);
      IMethodSymbol? method = symbolInfo.Symbol as IMethodSymbol;
      if (method is null)
      {
        foreach (var candidate in symbolInfo.CandidateSymbols)
        {
          if (candidate is IMethodSymbol m) { method = m; break; }
        }
      }

      if (method is null) return null;
      if (method.Name != "Wrap" || method.ContainingType?.Name != "Mock") return null;
      if (method.ContainingType.ContainingNamespace is not { Name: "Mocking", ContainingNamespace.Name: "Assertive" }) return null;
      if (method.TypeArguments.Length != 1 || method.TypeArguments[0] is not INamedTypeSymbol target) return null;

      // Only non-generic interfaces
      if (target.TypeKind != TypeKind.Interface || target.IsGenericType) return null;

      var mockTarget = BuildTarget(target, out _);
      return new WrapTarget(mockTarget, SanitizedWrapName(target));
    }

    private static string SanitizedWrapName(INamedTypeSymbol iface)
    {
      var mockName = SanitizedName(iface); // "Mock_Foo_IBar"
      return "Wrap_" + mockName.Substring("Mock_".Length);
    }

    private static void EmitWrapClass(StringBuilder sb, WrapTarget wrapTarget)
    {
      var target = wrapTarget.Target;
      var wrapName = wrapTarget.WrapClassName;

      sb.AppendLine($"  internal sealed class {wrapName} : global::Assertive.Mocking.MockBase, {target.InterfaceFqn}");
      sb.AppendLine("  {");
      sb.AppendLine($"    private readonly {target.InterfaceFqn} __wrapped;");
      sb.AppendLine($"    public {wrapName}({target.InterfaceFqn} __wrapped) : base(\"{target.DisplayName}\")");
      sb.AppendLine("      => this.__wrapped = __wrapped;");

      foreach (var method in target.Methods)
      {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var argNames = string.Join(", ", method.Parameters.Select(p => p.Name));
        var argsArray = method.Parameters.Length == 0
          ? "global::System.Array.Empty<object>()"
          : $"new object[] {{ {argNames} }}";

        if (method.ReturnKind == ReturnKind.Void)
        {
          sb.AppendLine($"    public void {method.Name}({parameters})");
          sb.AppendLine("    {");
          sb.AppendLine($"      if (OnCall(\"{method.Name}\", {argsArray}, out var __r, out var __matched)) return;");
          sb.AppendLine("      if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception;");
          sb.AppendLine($"      if (!__matched) __wrapped.{method.Name}({argNames});");
          sb.AppendLine("    }");
        }
        else
        {
          sb.AppendLine($"    public {method.ReturnTypeFqn} {method.Name}({parameters})");
          sb.AppendLine("    {");
          sb.AppendLine($"      var __args = {argsArray};");
          sb.AppendLine($"      if (OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine("      if (__matched)");
          sb.AppendLine("      {");
          sb.AppendLine($"        if (__r is global::Assertive.Mocking.MockFault __f) {FaultReturn(method)}");
          sb.AppendLine($"        return {ReturnIsExpr(method.ReturnTypeFqn!, MatchedFallback(method))};");
          sb.AppendLine("      }");
          sb.AppendLine($"      return __wrapped.{method.Name}({argNames});");
          sb.AppendLine("    }");
        }
      }

      foreach (var property in target.Properties)
      {
        sb.AppendLine($"    public {property.TypeFqn} {property.Name}");
        sb.AppendLine("    {");
        sb.AppendLine("      get");
        sb.AppendLine("      {");
        sb.AppendLine($"        var __args = global::System.Array.Empty<object>();");
        sb.AppendLine($"        if (OnCall(\"get_{property.Name}\", __args, out var __r, out var __matched)) return default;");
        sb.AppendLine("        if (__matched)");
        sb.AppendLine("        {");
        sb.AppendLine($"          if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception;");
        sb.AppendLine($"          return {ReturnIsExpr(property.TypeFqn, "default")};");
        sb.AppendLine("        }");
        sb.AppendLine($"        return __wrapped.{property.Name};");
        sb.AppendLine("      }");
        if (property.HasSetter)
          sb.AppendLine($"      set {{ if (OnCall(\"set_{property.Name}\", new object[] {{ value }}, out _, out _)) return; __wrapped.{property.Name} = value; }}");
        sb.AppendLine("    }");
      }

      foreach (var indexer in target.Indexers)
      {
        var parameters = string.Join(", ", indexer.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var argNames = string.Join(", ", indexer.Parameters.Select(p => p.Name));
        var argsArray = $"new object[] {{ {argNames} }}";

        sb.AppendLine($"    public {indexer.ReturnTypeFqn} this[{parameters}]");
        sb.AppendLine("    {");
        if (indexer.HasGetter)
        {
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = {argsArray};");
          sb.AppendLine($"        if (OnCall(\"get_Item\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine($"        if (__matched) {{ if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception; return {ReturnIsExpr(indexer.ReturnTypeFqn, "default")}; }}");
          sb.AppendLine($"        return __wrapped[{argNames}];");
          sb.AppendLine("      }");
        }
        if (indexer.HasSetter)
        {
          sb.AppendLine("      set");
          sb.AppendLine("      {");
          sb.AppendLine($"        if (OnCall(\"set_Item\", new object[] {{ {argNames}, value }}, out _, out _)) return;");
          sb.AppendLine($"        __wrapped[{argNames}] = value;");
          sb.AppendLine("      }");
        }
        sb.AppendLine("    }");
      }

      sb.AppendLine("  }");
    }

    private static BuildCall? ExtractBuildCall(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ct);
      IMethodSymbol? method = symbolInfo.Symbol as IMethodSymbol;
      if (method is null)
      {
        foreach (var candidate in symbolInfo.CandidateSymbols)
        {
          if (candidate is IMethodSymbol m) { method = m; break; }
        }
      }

      if (method is null) return null;
      if (method.Name != "Build" || method.ContainingType?.Name != "Mock") return null;
      if (method.ContainingType.ContainingNamespace is not { Name: "Mocking", ContainingNamespace.Name: "Assertive" }) return null;
      if (method.TypeArguments.Length != 1 || method.TypeArguments[0] is not INamedTypeSymbol target) return null;

      // T must be a concrete, non-static class
      if (target.TypeKind != TypeKind.Class || target.IsAbstract || target.IsStatic) return null;

      // Pick the richest accessible constructor
      var ctor = target.InstanceConstructors
        .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
        .OrderByDescending(c => c.Parameters.Length)
        .FirstOrDefault();

      if (ctor is null) return null;

      // Resolve the declared type of each provided argument at the call site
      var args = invocation.ArgumentList.Arguments;
      var argTypes = args.Select(a => ctx.SemanticModel.GetTypeInfo(a.Expression, ct).Type).ToArray();

      // Match each constructor param to the first unmatched provided arg by type
      var usedArgIndices = new HashSet<int>();
      var buildParams = new List<BuildParam>();
      var autoMockedTypes = new List<INamedTypeSymbol>();

      foreach (var param in ctor.Parameters)
      {
        int matchedArgIndex = -1;
        for (int i = 0; i < argTypes.Length; i++)
        {
          if (usedArgIndices.Contains(i) || argTypes[i] is null) continue;
          if (IsAssignableTo(argTypes[i]!, param.Type))
          {
            matchedArgIndex = i;
            usedArgIndices.Add(i);
            break;
          }
        }

        string? mockClassName = null;
        if (matchedArgIndex < 0 && param.Type is INamedTypeSymbol paramNamed && IsAutoMockable(paramNamed))
        {
          autoMockedTypes.Add(paramNamed);
          mockClassName = SanitizedName(paramNamed);
        }

        buildParams.Add(new BuildParam(
          param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          param.Name,
          matchedArgIndex,
          mockClassName));
      }

      var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);
      if (location is null) return null;

      // Collect mock targets for the auto-mocked parameter types (including their closures)
      var autoMockedTargets = autoMockedTypes
        .SelectMany(t => BuildClosure(t))
        .GroupBy(t => t.InterfaceFqn)
        .Select(g => g.First())
        .ToImmutableArray();

      return new BuildCall(
        target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        target.Name,
        buildParams.ToImmutableArray(),
        autoMockedTargets,
        location.Version,
        location.Data,
        location.GetDisplayLocation(),
        args.Count);
    }

    private static bool IsAssignableTo(ITypeSymbol from, ITypeSymbol to)
    {
      if (SymbolEqualityComparer.Default.Equals(from, to)) return true;

      if (to is { TypeKind: TypeKind.Interface })
      {
        return from.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, to));
      }

      for (var cur = from.BaseType; cur != null; cur = cur.BaseType)
      {
        if (SymbolEqualityComparer.Default.Equals(cur, to)) return true;
      }

      return false;
    }

    private static void EmitBuildInterceptors(StringBuilder sb, List<BuildCall> calls)
    {
      if (calls.Count == 0) return;

      sb.AppendLine("  internal static class __BuildInterceptors");
      sb.AppendLine("  {");

      for (int i = 0; i < calls.Count; i++)
      {
        var call = calls[i];
        var paramList = string.Join(", ", Enumerable.Range(0, call.Arity).Select(j => $"object? __a{j}"));

        var ctorArgs = string.Join(", ", call.Params.Select(p =>
        {
          if (p.ArgIndex >= 0) return $"({p.TypeFqn})__a{p.ArgIndex}!";
          if (p.MockClassName != null) return $"({p.TypeFqn})global::Assertive.Mocking.MockFactoryRegistry.Create<{p.TypeFqn}>(global::System.Array.Empty<object>())";
          return $"default({p.TypeFqn})";
        }));

        sb.AppendLine($"    // {call.DisplayLocation}");
        sb.AppendLine($"    [global::System.Runtime.CompilerServices.InterceptsLocation({call.LocationVersion}, {SymbolDisplay.FormatLiteral(call.LocationData, true)})]");
        sb.AppendLine($"    internal static {call.TargetFqn} __Build_{i}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"      return new {call.TargetFqn}({ctorArgs});");
        sb.AppendLine("    }");
      }

      sb.AppendLine("  }");
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<ImmutableArray<MockTarget>> arranged, ImmutableArray<MatcherCall?> matcherCalls, ImmutableArray<BuildCall?> buildCallsRaw, ImmutableArray<WrapTarget?> wrapTargetsRaw)
    {
      var builds = buildCallsRaw.Where(c => c is not null).Select(c => c!).ToList();
      var wraps = wrapTargetsRaw.Where(w => w is not null).Select(w => w!)
        .GroupBy(w => w.Target.InterfaceFqn)
        .Select(g => g.First())
        .ToList();

      var distinct = arranged
        .SelectMany(closure => closure)
        .Concat(builds.SelectMany(b => b.AutoMockedTargets))
        .GroupBy(t => t.InterfaceFqn)
        .Select(g => g.First())
        .ToList();

      var matchers = matcherCalls.Where(c => c is not null).Select(c => c!).ToList();

      if (distinct.Count == 0 && matchers.Count == 0 && builds.Count == 0 && wraps.Count == 0)
      {
        return;
      }

      var sb = new StringBuilder();

      sb.AppendLine("// <auto-generated/>");
      sb.AppendLine("#nullable disable");
      sb.AppendLine("#pragma warning disable");
      sb.AppendLine("global using static global::Assertive.Mocking.Generated.MockArrange;");
      sb.AppendLine();

      if (matchers.Count > 0 || builds.Count > 0)
      {
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("  [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("  file sealed class InterceptsLocationAttribute : global::System.Attribute");
        sb.AppendLine("  {");
        sb.AppendLine("    public InterceptsLocationAttribute(int version, string data) { _ = version; _ = data; }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
      }

      sb.AppendLine("namespace Assertive.Mocking.Generated");
      sb.AppendLine("{");

      EmitMatcherInterceptors(sb, matchers);
      EmitBuildInterceptors(sb, builds);

      foreach (var target in distinct)
      {
        EmitMockClass(sb, target);
      }

      foreach (var wrap in wraps)
      {
        EmitWrapClass(sb, wrap);
      }

      sb.AppendLine("  internal static class __MockRegistration");
      sb.AppendLine("  {");
      sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
      sb.AppendLine("    internal static void Init()");
      sb.AppendLine("    {");

      foreach (var target in distinct)
      {
        sb.AppendLine($"      global::Assertive.Mocking.MockFactoryRegistry.Register(typeof({target.InterfaceFqn}), {Factory(target)});");
      }

      foreach (var wrap in wraps)
      {
        sb.AppendLine($"      global::Assertive.Mocking.WrapFactoryRegistry.Register(typeof({wrap.Target.InterfaceFqn}), __w => new {wrap.WrapClassName}(({wrap.Target.InterfaceFqn})__w));");
      }

      sb.AppendLine("    }");
      sb.AppendLine("  }");
      sb.AppendLine();

      // Include wrap targets' methods in the Any(methodGroup) overloads so that
      // Arrange(spy, s => Any(s.Method).Returns(...)) resolves correctly.
      // Brought into scope via generated "global using static MockArrange" — no user import needed.
      EmitArrangeOverloads(sb, distinct.Concat(wraps.Select(w => w.Target)).ToList());

      sb.AppendLine("}");

      spc.AddSource("AssertiveMocks.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Emits non-generic <c>Any(methodGroup)</c> overloads into <c>MockArrange</c>, one per
    /// distinct method signature. They're what make <c>Any(mock.Method)</c> resolve (method
    /// groups don't drive generic inference), each forwarding to the runtime's generic Any with a
    /// delegate-typed argument (where inference does work). Brought into scope automatically via
    /// a generated <c>global using static</c> — no explicit import needed by the user.
    /// </summary>
    private static void EmitArrangeOverloads(StringBuilder sb, List<MockTarget> targets)
    {
      var emitted = new HashSet<string>();
      var lines = new List<string>();

      foreach (var method in targets.SelectMany(t => t.Methods))
      {
        if (BuildOverload(method) is not { } overload)
        {
          continue;
        }

        // Dedup by delegate type: one overload serves every method sharing that signature
        // (Resolve reads the actual method name from the delegate at runtime).
        if (emitted.Add(overload.DelegateType))
        {
          var typeArgs = overload.TypeArgs.Length > 0 ? $"<{overload.TypeArgs}>" : "";
          lines.Add($"    public static {overload.BuilderType} Any({overload.DelegateType} method) => global::Assertive.Mocking.Mock.Any{typeArgs}(method);");
        }
      }

      if (lines.Count == 0)
      {
        return;
      }

      sb.AppendLine("  internal static class MockArrange");
      sb.AppendLine("  {");

      foreach (var line in lines)
      {
        sb.AppendLine(line);
      }

      sb.AppendLine("  }");
    }

    /// <summary>
    /// Emits an extension-method interceptor per matcher-bearing mock call. Each builds the
    /// per-argument matcher array (Any / dequeued predicate / exact-value equality) and captures
    /// it on the mock, so the following arrange verb / When / Received uses those matchers.
    /// </summary>
    private static void EmitMatcherInterceptors(StringBuilder sb, List<MatcherCall> calls)
    {
      if (calls.Count == 0)
      {
        return;
      }

      sb.AppendLine("  internal static class MockMatchers");
      sb.AppendLine("  {");

      var index = 0;

      foreach (var call in calls)
      {
        var parameters = string.Join(", ", call.ParameterTypes.Select((t, i) => $"{t} __a{i}"));
        var returnType = call.ReturnTypeFqn ?? "void";

        var matcherExprs = call.ArgumentKinds.Select((kind, i) => kind switch
        {
          ArgKind.Any => "static (object __x) => true",
          ArgKind.Predicate => "global::Assertive.Mocking.Mock.DequeueMatcher()",
          _ => $"(object __x) => global::System.Object.Equals(__x, (object)__a{i})",
        });

        var displayArgs = string.Join(", ", Enumerable.Range(0, call.ParameterTypes.Length).Select(i => $"(object)__a{i}"));

        sb.AppendLine($"    // {call.DisplayLocation}");
        sb.AppendLine($"    [global::System.Runtime.CompilerServices.InterceptsLocation({call.LocationVersion}, {SymbolDisplay.FormatLiteral(call.LocationData, true)})]");
        sb.AppendLine($"    public static {returnType} Match_{index}(this {call.ReceiverFqn} __r, {parameters})");
        sb.AppendLine("    {");
        sb.AppendLine("      var __m = ((global::Assertive.Mocking.IMockObject)(object)__r).Core;");
        sb.AppendLine($"      __m.CaptureMatchers(\"{call.Method}\", new global::System.Func<object, bool>[] {{ {string.Join(", ", matcherExprs)} }}, new object[] {{ {displayArgs} }});");

        if (call.ReturnTypeFqn != null)
        {
          sb.AppendLine("      return default;");
        }

        sb.AppendLine("    }");
        index++;
      }

      sb.AppendLine("  }");
    }

    /// <summary>Maps a method to its (delegate type, builder type, explicit type args), or null for arities the runtime builders don't cover.</summary>
    private static (string DelegateType, string BuilderType, string TypeArgs)? BuildOverload(MockMethod method)
    {
      var ps = method.Parameters;

      if (method.ReturnTypeFqn is null)
      {
        return ps.Length switch
        {
          0 => ("global::System.Action", "global::Assertive.Mocking.VoidArrange", ""),
          1 => ($"global::System.Action<{ps[0].Type}>", $"global::Assertive.Mocking.VoidArrange<{ps[0].Type}>", $"{ps[0].Type}"),
          2 => ($"global::System.Action<{ps[0].Type}, {ps[1].Type}>", $"global::Assertive.Mocking.VoidArrange<{ps[0].Type}, {ps[1].Type}>", $"{ps[0].Type}, {ps[1].Type}"),
          3 => ($"global::System.Action<{ps[0].Type}, {ps[1].Type}, {ps[2].Type}>", $"global::Assertive.Mocking.VoidArrange<{ps[0].Type}, {ps[1].Type}, {ps[2].Type}>", $"{ps[0].Type}, {ps[1].Type}, {ps[2].Type}"),
          _ => ((string, string, string)?)null,
        };
      }

      var ret = method.ReturnTypeFqn;

      return ps.Length switch
      {
        0 => ($"global::System.Func<{ret}>", $"global::Assertive.Mocking.ValueArrange<{ret}>", $"{ret}"),
        1 => ($"global::System.Func<{ps[0].Type}, {ret}>", $"global::Assertive.Mocking.ValueArrange<{ps[0].Type}, {ret}>", $"{ps[0].Type}, {ret}"),
        2 => ($"global::System.Func<{ps[0].Type}, {ps[1].Type}, {ret}>", $"global::Assertive.Mocking.ValueArrange<{ps[0].Type}, {ps[1].Type}, {ret}>", $"{ps[0].Type}, {ps[1].Type}, {ret}"),
        3 => ($"global::System.Func<{ps[0].Type}, {ps[1].Type}, {ps[2].Type}, {ret}>", $"global::Assertive.Mocking.ValueArrange<{ps[0].Type}, {ps[1].Type}, {ps[2].Type}, {ret}>", $"{ps[0].Type}, {ps[1].Type}, {ps[2].Type}, {ret}"),
        4 => ($"global::System.Func<{ps[0].Type}, {ps[1].Type}, {ps[2].Type}, {ps[3].Type}, {ret}>", $"global::Assertive.Mocking.ValueArrange<{ps[0].Type}, {ps[1].Type}, {ps[2].Type}, {ps[3].Type}, {ret}>", $"{ps[0].Type}, {ps[1].Type}, {ps[2].Type}, {ps[3].Type}, {ret}"),
        _ => null,
      };
    }

    /// <summary>The registry factory: ignores args for interfaces; dispatches on argument count to a forwarding ctor for classes.</summary>
    private static string Factory(MockTarget target)
    {
      if (!target.IsClass || target.Constructors.Length == 0)
      {
        return $"static __a => new {target.ClassName}()";
      }

      var arms = target.Constructors
        .GroupBy(c => c.Length)
        .Select(g => g.First())
        .Select(c =>
        {
          var cast = string.Join(", ", c.Select((t, i) => $"({t})__a[{i}]"));
          return $"{c.Length} => new {target.ClassName}({cast})";
        });

      return $"static __a => __a.Length switch {{ {string.Join(", ", arms)}, _ => throw new global::System.InvalidOperationException(\"Assertive.Mocking: no {target.DisplayName} constructor takes \" + __a.Length + \" argument(s).\") }}";
    }

    private static void EmitMockClass(StringBuilder sb, MockTarget target)
    {
      // Interface mocks inherit MockBase (the engine); class mocks must extend the mocked class,
      // so they compose a MockBase and reach it through IMockObject.
      var core = target.IsClass ? "__core." : "";
      var modifier = target.IsClass ? "public override " : "public ";

      sb.AppendLine(target.IsClass
        ? $"  internal sealed class {target.ClassName} : {target.InterfaceFqn}, global::Assertive.Mocking.IMockObject"
        : $"  internal sealed class {target.ClassName} : global::Assertive.Mocking.MockBase, {target.InterfaceFqn}");
      sb.AppendLine("  {");

      if (target.IsClass)
      {
        sb.AppendLine($"    private readonly global::Assertive.Mocking.MockBase __core = new global::Assertive.Mocking.MockBase(\"{target.DisplayName}\");");
        sb.AppendLine("    global::Assertive.Mocking.MockBase global::Assertive.Mocking.IMockObject.Core => __core;");

        foreach (var ctor in target.Constructors)
        {
          var ctorParams = string.Join(", ", ctor.Select((t, i) => $"{t} __c{i}"));
          var baseArgs = string.Join(", ", Enumerable.Range(0, ctor.Length).Select(i => $"__c{i}"));
          sb.AppendLine(ctor.Length == 0
            ? $"    public {target.ClassName}() {{ }}"
            : $"    public {target.ClassName}({ctorParams}) : base({baseArgs}) {{ }}");
        }
      }
      else
      {
        sb.AppendLine($"    public {target.ClassName}() : base(\"{target.DisplayName}\") {{ }}");
      }

      foreach (var method in target.Methods)
      {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var argNames = string.Join(", ", method.Parameters.Select(p => p.Name));
        var argsArray = method.Parameters.Length == 0
          ? "global::System.Array.Empty<object>()"
          : $"new object[] {{ {argNames} }}";

        if (method.ReturnKind == ReturnKind.Void)
        {
          sb.AppendLine($"    {modifier}void {method.Name}({parameters})");
          sb.AppendLine("    {");
          sb.AppendLine($"      if ({core}OnCall(\"{method.Name}\", {argsArray}, out var __r, out var __matched)) return;");
          sb.AppendLine("      if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception;");
          if (method.CallBase)
          {
            sb.AppendLine($"      if (!__matched) base.{method.Name}({argNames});");
          }

          sb.AppendLine("    }");
          continue;
        }

        var unarranged = method.CallBase ? $"base.{method.Name}({argNames})" : UnarrangedReturn(method, core);

        sb.AppendLine($"    {modifier}{method.ReturnTypeFqn} {method.Name}({parameters})");
        sb.AppendLine("    {");
        sb.AppendLine($"      var __args = {argsArray};");
        sb.AppendLine($"      if ({core}OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) return default;");
        sb.AppendLine("      if (__matched)");
        sb.AppendLine("      {");
        sb.AppendLine($"        if (__r is global::Assertive.Mocking.MockFault __f) {FaultReturn(method)}");
        sb.AppendLine($"        return {ReturnIsExpr(method.ReturnTypeFqn!, MatchedFallback(method))};");
        sb.AppendLine("      }");
        sb.AppendLine($"      return {unarranged};");
        sb.AppendLine("    }");
      }

      foreach (var property in target.Properties)
      {
        if (!target.IsClass)
        {
          // Interface properties: instrument the getter so arrangements and Received() work,
          // and record setter calls. The getter returns default when unarranged (no base to call).
          sb.AppendLine($"    public {property.TypeFqn} {property.Name}");
          sb.AppendLine("    {");
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = global::System.Array.Empty<object>();");
          sb.AppendLine($"        if (OnCall(\"get_{property.Name}\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine("        if (__matched)");
          sb.AppendLine("        {");
          sb.AppendLine($"          if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception;");
          sb.AppendLine($"          return {ReturnIsExpr(property.TypeFqn, "default")};");
          sb.AppendLine("        }");
          sb.AppendLine("        return default;");
          sb.AppendLine("      }");
          if (property.HasSetter)
            sb.AppendLine($"      set {{ OnCall(\"set_{property.Name}\", new object[] {{ value }}, out _, out _); }}");
          sb.AppendLine("    }");
        }
        else
        {
          var unarrangedPropReturn = property.CallBase ? $"base.{property.Name}" : "default";
          sb.AppendLine($"    public override {property.TypeFqn} {property.Name}");
          sb.AppendLine("    {");
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = global::System.Array.Empty<object>();");
          sb.AppendLine($"        if ({core}OnCall(\"get_{property.Name}\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine("        if (__matched)");
          sb.AppendLine("        {");
          sb.AppendLine($"          if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception;");
          sb.AppendLine($"          return {ReturnIsExpr(property.TypeFqn, "default")};");
          sb.AppendLine("        }");
          sb.AppendLine($"        return {unarrangedPropReturn};");
          sb.AppendLine("      }");
          if (property.HasSetter)
            sb.AppendLine($"      set {{ {core}OnCall(\"set_{property.Name}\", new object[] {{ value }}, out _, out _); }}");
          sb.AppendLine("    }");
        }
      }

      foreach (var stub in target.GenericStubs)
        sb.AppendLine(stub);

      foreach (var indexer in target.Indexers)
      {
        var parameters = string.Join(", ", indexer.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var argNames = string.Join(", ", indexer.Parameters.Select(p => p.Name));
        var argsArray = $"new object[] {{ {argNames} }}";
        var idxModifier = target.IsClass ? "public override " : "public ";

        sb.AppendLine($"    {idxModifier}{indexer.ReturnTypeFqn} this[{parameters}]");
        sb.AppendLine("    {");
        if (indexer.HasGetter)
        {
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = {argsArray};");
          sb.AppendLine($"        if ({core}OnCall(\"get_Item\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine($"        if (__matched) {{ if (__r is global::Assertive.Mocking.MockFault __f) throw __f.Exception; return {ReturnIsExpr(indexer.ReturnTypeFqn, "default")}; }}");
          var unarrangedIdx = indexer.CallBase ? $"base[{argNames}]" : "default";
          sb.AppendLine($"        return {unarrangedIdx};");
          sb.AppendLine("      }");
        }
        if (indexer.HasSetter)
        {
          sb.AppendLine("      set");
          sb.AppendLine("      {");
          sb.AppendLine($"        {core}OnCall(\"set_Item\", new object[] {{ {argNames}, value }}, out _, out _);");
          sb.AppendLine("      }");
        }
        sb.AppendLine("    }");
      }

      foreach (var ev in target.Events)
      {
        var evModifier = target.IsClass ? "public override " : "public ";
        sb.AppendLine($"    {evModifier}event {ev.HandlerTypeFqn} {ev.Name}");
        sb.AppendLine("    {");
        sb.AppendLine($"      add    {{ if ({core}TryCaptureEvent(\"{ev.Name}\")) return; {core}AddEventHandler(\"{ev.Name}\", value); }}");
        sb.AppendLine($"      remove {{ {core}RemoveEventHandler(\"{ev.Name}\", value); }}");
        sb.AppendLine("    }");
      }

      sb.AppendLine("  }");
    }

    /// <summary>The expression returned when a method is called but not arranged (the smart default).</summary>
    private static string UnarrangedReturn(MockMethod method, string core) => method.ReturnKind switch
    {
      ReturnKind.AutoMock => $"({method.ReturnTypeFqn}){core}GetOrCreateAutoMock(\"{method.Name}\", __args, static () => new {method.DirectMockClassName}())",
      ReturnKind.Task => "global::System.Threading.Tasks.Task.CompletedTask",
      ReturnKind.ValueTask => "default",
      ReturnKind.TaskOfT => $"global::System.Threading.Tasks.Task.FromResult<{method.InnerTypeFqn}>({InnerValue(method, core)})",
      ReturnKind.ValueTaskOfT => $"new global::System.Threading.Tasks.ValueTask<{method.InnerTypeFqn}>({InnerValue(method, core)})",
      ReturnKind.EmptyCollection => EmptyCollectionExpression(method),
      _ => "default",
    };

    /// <summary>
    /// Emits an appropriate empty-collection expression for unarranged collection-returning methods.
    /// Concrete <c>List&lt;T&gt;</c> gets <c>new List&lt;T&gt;()</c>; everything else (arrays, interfaces)
    /// gets <c>Array.Empty&lt;T&gt;()</c> which is assignable to all IEnumerable variants.
    /// </summary>
    private static string EmptyCollectionExpression(MockMethod method)
    {
      // Detect a concrete List<T> return (as opposed to IEnumerable<T>, IList<T>, T[], etc.).
      var rtFqn = method.ReturnTypeFqn ?? "";
      if (rtFqn.StartsWith("global::System.Collections.Generic.List<", StringComparison.Ordinal))
      {
        return $"new global::System.Collections.Generic.List<{method.ElementTypeFqn}>()";
      }

      return $"global::System.Array.Empty<{method.ElementTypeFqn}>()";
    }

    /// <summary>
    /// Emits the cast expression for an unboxed return value.
    /// C# does not allow <c>is T?</c> patterns for nullable value types, so we strip the trailing <c>?</c>
    /// from the pattern type and cast the result to the full return type (which is a no-op for reference types,
    /// and an implicit upcast for value types like <c>double</c> → <c>double?</c>).
    /// </summary>
    private static string ReturnIsExpr(string returnTypeFqn, string fallback)
    {
      // Strip trailing '?' to get the pattern-matchable type (works for both nullable value types and
      // nullable reference types — for NRT the cast back is a no-op widening).
      var patternType = returnTypeFqn.EndsWith("?", StringComparison.Ordinal)
        ? returnTypeFqn.Substring(0, returnTypeFqn.Length - 1)
        : returnTypeFqn;
      var cast = patternType != returnTypeFqn ? $"({returnTypeFqn})" : "";
      return $"(__r is {patternType} __v) ? {cast}__v : {fallback}";
    }

    /// <summary>How an arranged <c>.Throws</c> (a MockFault) surfaces: a synchronous throw, or a faulted task for async returns.</summary>
    private static string FaultReturn(MockMethod method) => method.ReturnKind switch
    {
      ReturnKind.Task => "return global::System.Threading.Tasks.Task.FromException(__f.Exception);",
      ReturnKind.ValueTask => "return new global::System.Threading.Tasks.ValueTask(global::System.Threading.Tasks.Task.FromException(__f.Exception));",
      ReturnKind.TaskOfT => $"return global::System.Threading.Tasks.Task.FromException<{method.InnerTypeFqn}>(__f.Exception);",
      ReturnKind.ValueTaskOfT => $"return new global::System.Threading.Tasks.ValueTask<{method.InnerTypeFqn}>(global::System.Threading.Tasks.Task.FromException<{method.InnerTypeFqn}>(__f.Exception));",
      _ => "throw __f.Exception;",
    };

    /// <summary>The matched-but-not-the-expected-type fallback (e.g. a Task method returns a completed task).</summary>
    private static string MatchedFallback(MockMethod method) => method.ReturnKind switch
    {
      ReturnKind.Task => "global::System.Threading.Tasks.Task.CompletedTask",
      _ => "default",
    };

    /// <summary>The completed-task result: an auto-mock child when the inner type is mockable, else default(T).</summary>
    private static string InnerValue(MockMethod method, string core) => method.InnerMockClassName is { } innerMock
      ? $"({method.InnerTypeFqn}){core}GetOrCreateAutoMock(\"{method.Name}\", __args, static () => new {innerMock}())"
      : $"default({method.InnerTypeFqn})";
  }

  internal enum ArgKind
  {
    Exact,
    Any,
    Predicate,
  }

  internal sealed class MatcherCall : IEquatable<MatcherCall>
  {
    public MatcherCall(string receiverFqn, string method, string? returnTypeFqn, ImmutableArray<string> parameterTypes,
      ImmutableArray<ArgKind> argumentKinds, int locationVersion, string locationData, string displayLocation)
    {
      ReceiverFqn = receiverFqn;
      Method = method;
      ReturnTypeFqn = returnTypeFqn;
      ParameterTypes = parameterTypes;
      ArgumentKinds = argumentKinds;
      LocationVersion = locationVersion;
      LocationData = locationData;
      DisplayLocation = displayLocation;
    }

    public string ReceiverFqn { get; }
    public string Method { get; }
    public string? ReturnTypeFqn { get; }
    public ImmutableArray<string> ParameterTypes { get; }
    public ImmutableArray<ArgKind> ArgumentKinds { get; }
    public int LocationVersion { get; }
    public string LocationData { get; }
    public string DisplayLocation { get; }

    public bool Equals(MatcherCall? other)
    {
      if (other is null) return false;
      if (ReceiverFqn != other.ReceiverFqn) return false;
      if (Method != other.Method) return false;
      if (ReturnTypeFqn != other.ReturnTypeFqn) return false;
      if (LocationVersion != other.LocationVersion) return false;
      if (LocationData != other.LocationData) return false;
      if (DisplayLocation != other.DisplayLocation) return false;
      if (!ParameterTypes.SequenceEqual(other.ParameterTypes)) return false;
      if (!ArgumentKinds.SequenceEqual(other.ArgumentKinds)) return false;
      return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MatcherCall);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = ReceiverFqn?.GetHashCode() ?? 0;
        h = h * 31 + (Method?.GetHashCode() ?? 0);
        h = h * 31 + (LocationData?.GetHashCode() ?? 0);
        h = h * 31 + LocationVersion;
        return h;
      }
    }
  }

  internal sealed class MockTarget : IEquatable<MockTarget>
  {
    public MockTarget(string interfaceFqn, string className, string displayName, bool isClass,
      ImmutableArray<MockMethod> methods, ImmutableArray<MockProperty> properties, ImmutableArray<ImmutableArray<string>> constructors,
      ImmutableArray<MockIndexer> indexers = default, ImmutableArray<MockEvent> events = default,
      ImmutableArray<string> genericStubs = default)
    {
      InterfaceFqn = interfaceFqn;
      ClassName = className;
      DisplayName = displayName;
      IsClass = isClass;
      Methods = methods;
      Properties = properties;
      Constructors = constructors;
      Indexers = indexers.IsDefault ? ImmutableArray<MockIndexer>.Empty : indexers;
      Events = events.IsDefault ? ImmutableArray<MockEvent>.Empty : events;
      GenericStubs = genericStubs.IsDefault ? ImmutableArray<string>.Empty : genericStubs;
    }

    /// <summary>FQN of the mocked type (interface or class).</summary>
    public string InterfaceFqn { get; }
    public string ClassName { get; }
    public string DisplayName { get; }
    public bool IsClass { get; }
    public ImmutableArray<MockMethod> Methods { get; }
    public ImmutableArray<MockProperty> Properties { get; }
    public ImmutableArray<MockIndexer> Indexers { get; }
    public ImmutableArray<MockEvent> Events { get; }
    /// <summary>Verbatim C# lines for generic interface methods that can't be intercepted but must be implemented.</summary>
    public ImmutableArray<string> GenericStubs { get; }

    /// <summary>For a class: each accessible base constructor's parameter type FQNs (for forwarding ctors).</summary>
    public ImmutableArray<ImmutableArray<string>> Constructors { get; }

    public bool Equals(MockTarget? other)
    {
      if (other is null) return false;
      if (InterfaceFqn != other.InterfaceFqn) return false;
      if (ClassName != other.ClassName) return false;
      if (DisplayName != other.DisplayName) return false;
      if (IsClass != other.IsClass) return false;
      if (!Methods.SequenceEqual(other.Methods)) return false;
      if (!Properties.SequenceEqual(other.Properties)) return false;
      if (!Indexers.SequenceEqual(other.Indexers)) return false;
      if (!Events.SequenceEqual(other.Events)) return false;
      if (!GenericStubs.SequenceEqual(other.GenericStubs)) return false;
      if (Constructors.Length != other.Constructors.Length) return false;
      for (var i = 0; i < Constructors.Length; i++)
        if (!Constructors[i].SequenceEqual(other.Constructors[i]))
          return false;
      return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MockTarget);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = InterfaceFqn?.GetHashCode() ?? 0;
        h = h * 31 + (ClassName?.GetHashCode() ?? 0);
        h = h * 31 + IsClass.GetHashCode();
        h = h * 31 + Methods.Length;
        h = h * 31 + Properties.Length;
        h = h * 31 + Indexers.Length;
        h = h * 31 + Events.Length;
        h = h * 31 + GenericStubs.Length;
        return h;
      }
    }
  }

  internal enum ReturnKind
  {
    Void,
    Value,           // plain non-mock value: default if unarranged
    AutoMock,        // interface: memoized child mock if unarranged
    Task,            // non-generic Task: completed task
    ValueTask,       // non-generic ValueTask: completed
    TaskOfT,         // Task<T>: completed task wrapping auto-mock(T) or default(T)
    ValueTaskOfT,    // ValueTask<T>: same, wrapped in ValueTask<T>
    EmptyCollection, // IEnumerable<T>/IList<T>/List<T>/T[] etc.: Array.Empty<T>() if unarranged
  }

  internal sealed class MockMethod : IEquatable<MockMethod>
  {
    public MockMethod(string name, string? returnTypeFqn, (string Type, string Name)[] parameters,
      ReturnKind returnKind, string? innerTypeFqn, string? innerMockClassName, string? directMockClassName, bool callBase,
      string? elementTypeFqn = null)
    {
      Name = name;
      ReturnTypeFqn = returnTypeFqn;
      Parameters = parameters;
      ReturnKind = returnKind;
      InnerTypeFqn = innerTypeFqn;
      InnerMockClassName = innerMockClassName;
      DirectMockClassName = directMockClassName;
      CallBase = callBase;
      ElementTypeFqn = elementTypeFqn;
    }

    /// <summary>True for a non-abstract virtual class member: unarranged calls run the real base method.</summary>
    public bool CallBase { get; }

    public string Name { get; }
    public string? ReturnTypeFqn { get; }
    public (string Type, string Name)[] Parameters { get; }
    public ReturnKind ReturnKind { get; }

    /// <summary>For Task&lt;T&gt;/ValueTask&lt;T&gt;: the FQN of T.</summary>
    public string? InnerTypeFqn { get; }

    /// <summary>For Task&lt;T&gt;/ValueTask&lt;T&gt; where T is mockable: the mock class to instantiate.</summary>
    public string? InnerMockClassName { get; }

    /// <summary>For a direct interface return: the mock class to instantiate.</summary>
    public string? DirectMockClassName { get; }

    /// <summary>For EmptyCollection: the element type FQN (T in IEnumerable&lt;T&gt;/T[]/etc.).</summary>
    public string? ElementTypeFqn { get; }

    public bool Equals(MockMethod? other)
    {
      if (other is null) return false;
      if (Name != other.Name) return false;
      if (ReturnTypeFqn != other.ReturnTypeFqn) return false;
      if (ReturnKind != other.ReturnKind) return false;
      if (InnerTypeFqn != other.InnerTypeFqn) return false;
      if (InnerMockClassName != other.InnerMockClassName) return false;
      if (DirectMockClassName != other.DirectMockClassName) return false;
      if (ElementTypeFqn != other.ElementTypeFqn) return false;
      if (CallBase != other.CallBase) return false;
      if (Parameters.Length != other.Parameters.Length) return false;
      for (var i = 0; i < Parameters.Length; i++)
        if (Parameters[i].Type != other.Parameters[i].Type || Parameters[i].Name != other.Parameters[i].Name)
          return false;
      return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MockMethod);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = Name?.GetHashCode() ?? 0;
        h = h * 31 + (ReturnTypeFqn?.GetHashCode() ?? 0);
        h = h * 31 + ReturnKind.GetHashCode();
        h = h * 31 + (ElementTypeFqn?.GetHashCode() ?? 0);
        h = h * 31 + CallBase.GetHashCode();
        foreach (var p in Parameters)
        {
          h = h * 31 + (p.Type?.GetHashCode() ?? 0);
          h = h * 31 + (p.Name?.GetHashCode() ?? 0);
        }
        return h;
      }
    }
  }

  internal sealed class MockProperty : IEquatable<MockProperty>
  {
    public MockProperty(string name, string typeFqn, bool callBase = false, bool hasSetter = true)
    {
      Name = name;
      TypeFqn = typeFqn;
      CallBase = callBase;
      HasSetter = hasSetter;
    }

    public string Name { get; }
    public string TypeFqn { get; }
    public bool CallBase { get; }
    public bool HasSetter { get; }

    public bool Equals(MockProperty? other)
    {
      if (other is null) return false;
      return Name == other.Name && TypeFqn == other.TypeFqn && CallBase == other.CallBase && HasSetter == other.HasSetter;
    }

    public override bool Equals(object? obj) => Equals(obj as MockProperty);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = Name?.GetHashCode() ?? 0;
        h = h * 31 + (TypeFqn?.GetHashCode() ?? 0);
        h = h * 31 + CallBase.GetHashCode();
        h = h * 31 + HasSetter.GetHashCode();
        return h;
      }
    }
  }

  internal sealed class MockIndexer : IEquatable<MockIndexer>
  {
    public MockIndexer(string returnTypeFqn, (string Type, string Name)[] parameters, bool hasGetter, bool hasSetter, bool callBase)
    {
      ReturnTypeFqn = returnTypeFqn;
      Parameters = parameters;
      HasGetter = hasGetter;
      HasSetter = hasSetter;
      CallBase = callBase;
    }

    public string ReturnTypeFqn { get; }
    public (string Type, string Name)[] Parameters { get; }
    public bool HasGetter { get; }
    public bool HasSetter { get; }
    public bool CallBase { get; }

    public bool Equals(MockIndexer? other)
    {
      if (other is null) return false;
      if (ReturnTypeFqn != other.ReturnTypeFqn) return false;
      if (HasGetter != other.HasGetter) return false;
      if (HasSetter != other.HasSetter) return false;
      if (CallBase != other.CallBase) return false;
      if (Parameters.Length != other.Parameters.Length) return false;
      for (var i = 0; i < Parameters.Length; i++)
        if (Parameters[i].Type != other.Parameters[i].Type || Parameters[i].Name != other.Parameters[i].Name)
          return false;
      return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MockIndexer);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = ReturnTypeFqn?.GetHashCode() ?? 0;
        h = h * 31 + HasGetter.GetHashCode();
        h = h * 31 + HasSetter.GetHashCode();
        h = h * 31 + CallBase.GetHashCode();
        foreach (var p in Parameters)
        {
          h = h * 31 + (p.Type?.GetHashCode() ?? 0);
          h = h * 31 + (p.Name?.GetHashCode() ?? 0);
        }
        return h;
      }
    }
  }

  internal sealed class MockEvent : IEquatable<MockEvent>
  {
    public MockEvent(string name, string handlerTypeFqn)
    {
      Name = name;
      HandlerTypeFqn = handlerTypeFqn;
    }

    public string Name { get; }
    public string HandlerTypeFqn { get; }

    public bool Equals(MockEvent? other) => other is not null && Name == other.Name && HandlerTypeFqn == other.HandlerTypeFqn;

    public override bool Equals(object? obj) => obj is MockEvent e && Equals(e);

    public override int GetHashCode()
    {
      unchecked
      {
        return (Name?.GetHashCode() ?? 0) * 397 ^ (HandlerTypeFqn?.GetHashCode() ?? 0);
      }
    }
  }

  internal sealed class WrapTarget : IEquatable<WrapTarget>
  {
    public WrapTarget(MockTarget target, string wrapClassName)
    {
      Target = target;
      WrapClassName = wrapClassName;
    }

    /// <summary>The interface being wrapped — contains all member info needed to emit the wrap class.</summary>
    public MockTarget Target { get; }
    /// <summary>Generated class name for the wrap implementation (e.g. "Wrap_IFoo").</summary>
    public string WrapClassName { get; }

    public bool Equals(WrapTarget? other)
    {
      if (other is null) return false;
      return WrapClassName == other.WrapClassName && Target.Equals(other.Target);
    }

    public override bool Equals(object? obj) => Equals(obj as WrapTarget);

    public override int GetHashCode()
    {
      unchecked
      {
        return (Target?.InterfaceFqn?.GetHashCode() ?? 0) * 397 ^ (WrapClassName?.GetHashCode() ?? 0);
      }
    }
  }

  internal sealed class BuildParam : IEquatable<BuildParam>
  {
    public BuildParam(string typeFqn, string paramName, int argIndex, string? mockClassName)
    {
      TypeFqn = typeFqn;
      ParamName = paramName;
      ArgIndex = argIndex;
      MockClassName = mockClassName;
    }

    /// <summary>Fully-qualified type of the constructor parameter.</summary>
    public string TypeFqn { get; }
    public string ParamName { get; }
    /// <summary>Index into the Build&lt;T&gt; provided args (0-based), or -1 if this param is auto-mocked.</summary>
    public int ArgIndex { get; }
    /// <summary>Generated mock class name, set when ArgIndex is -1 and the type is auto-mockable.</summary>
    public string? MockClassName { get; }

    public bool Equals(BuildParam? other)
    {
      if (other is null) return false;
      return TypeFqn == other.TypeFqn && ParamName == other.ParamName && ArgIndex == other.ArgIndex && MockClassName == other.MockClassName;
    }

    public override bool Equals(object? obj) => Equals(obj as BuildParam);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = TypeFqn?.GetHashCode() ?? 0;
        h = h * 31 + (ParamName?.GetHashCode() ?? 0);
        h = h * 31 + ArgIndex;
        h = h * 31 + (MockClassName?.GetHashCode() ?? 0);
        return h;
      }
    }
  }

  internal sealed class BuildCall : IEquatable<BuildCall>
  {
    public BuildCall(string targetFqn, string displayName, ImmutableArray<BuildParam> buildParams,
      ImmutableArray<MockTarget> autoMockedTargets, int locationVersion, string locationData,
      string displayLocation, int arity)
    {
      TargetFqn = targetFqn;
      DisplayName = displayName;
      Params = buildParams;
      AutoMockedTargets = autoMockedTargets;
      LocationVersion = locationVersion;
      LocationData = locationData;
      DisplayLocation = displayLocation;
      Arity = arity;
    }

    /// <summary>FQN of the type being constructed (T in Build&lt;T&gt;).</summary>
    public string TargetFqn { get; }
    public string DisplayName { get; }
    /// <summary>One entry per constructor parameter, in constructor order.</summary>
    public ImmutableArray<BuildParam> Params { get; }
    /// <summary>Mock targets for auto-mocked constructor parameters (may include transitive closures).</summary>
    public ImmutableArray<MockTarget> AutoMockedTargets { get; }
    public int LocationVersion { get; }
    public string LocationData { get; }
    public string DisplayLocation { get; }
    /// <summary>Number of object? arguments in the Build&lt;T&gt; overload being intercepted.</summary>
    public int Arity { get; }

    public bool Equals(BuildCall? other)
    {
      if (other is null) return false;
      if (TargetFqn != other.TargetFqn) return false;
      if (DisplayName != other.DisplayName) return false;
      if (LocationVersion != other.LocationVersion) return false;
      if (LocationData != other.LocationData) return false;
      if (DisplayLocation != other.DisplayLocation) return false;
      if (Arity != other.Arity) return false;
      if (!Params.SequenceEqual(other.Params)) return false;
      if (!AutoMockedTargets.SequenceEqual(other.AutoMockedTargets)) return false;
      return true;
    }

    public override bool Equals(object? obj) => Equals(obj as BuildCall);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = TargetFqn?.GetHashCode() ?? 0;
        h = h * 31 + (LocationData?.GetHashCode() ?? 0);
        h = h * 31 + LocationVersion;
        h = h * 31 + Arity;
        return h;
      }
    }
  }
}
