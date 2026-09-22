using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
      title: "Cannot arrange a non-virtual member",
      messageFormat: "'{0}.{1}' is not virtual or abstract, so it cannot be arranged on a class mock (the real implementation would always run)",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Error,
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
      messageFormat: "Named arguments at mock call sites cannot be matched positionally: the matcher would silently bind the wrong argument. Use positional arguments instead.",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Mock005 = new DiagnosticDescriptor(
      id: "MOCK005",
      title: "Class mock has explicit interface implementations",
      messageFormat: "'{0}' explicitly implements {1}; those members cannot be arranged on a class mock and will run the real implementation",
      category: "Assertive.Mocking",
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Explicit interface implementations are private and cannot be intercepted by the generated subclass.");

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

      // MOCK005: warn when a mocked class has explicit interface implementations (not interceptable).
      var mock005Diagnostics = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsGenericCallNamed(node, "A"),
          transform: static (ctx, ct) => ExtractMock005(ctx, ct))
        .Where(static d => d is not null);

      context.RegisterSourceOutput(mock005Diagnostics.Collect(), static (spc, diags) =>
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

      // MOCK004: reject named arguments at a matcher call site inside an arrange context. Named
      // args cannot be mapped onto the positional matcher queue, so the arrangement would bind the
      // wrong argument; reject it loudly rather than silently mis-arranging.
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
      if (GetUnmockableReason(target) is not { } reason)
      {
        return null;
      }

      return Diagnostic.Create(Mock001, invocation.GetLocation(),
        target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        reason);
    }

    /// <summary>
    /// MOCK002 (error): an arrangement lambda arranges a non-virtual, non-abstract method on a class
    /// mock. The real implementation would always run, so the arrangement can never take effect.
    /// Only reported when the receiver can be proven to be the mock being arranged, to avoid false
    /// positives on real objects touched inside arrange lambdas.
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

      // Static calls are not instance arrangements on a class mock.
      if (method.IsStatic)
      {
        return null;
      }

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

      // System.Object members (GetType, GetHashCode, ...) are inherited by every class and can
      // never be arranged — touching one inside an arrange lambda is not a mistaken arrangement.
      if (containingType.SpecialType == SpecialType.System_Object)
      {
        return null;
      }

      // Virtual, abstract, and override members ARE interceptable; report only concrete non-virtual ones.
      if (method.IsVirtual || method.IsAbstract || method.IsOverride)
      {
        return null;
      }

      // Verify the receiver is the mock being arranged. Without this, calls on real class instances
      // inside an arrange lambda (e.g. list.Clear()) would be reported as errors.
      if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
      {
        return null;
      }

      var receiverType = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression, ct).Type as INamedTypeSymbol;
      if (receiverType is null)
      {
        return null;
      }

      var mockType = GetEnclosingMockType(ctx, invocation, ct);

      if (mockType is not null)
      {
        // A<T>(m => ...) / Setup(m, m => ...): the receiver must be the mocked type itself and
        // must actually be the arrange lambda's parameter — a real object of the same type touched
        // inside the lambda (e.g. `real.NonVirtual()`) is not the mock.
        if (!SymbolEqualityComparer.Default.Equals(receiverType, mockType)
            || !ReceiverIsArrangeLambdaParameter(ctx, invocation, ct))
        {
          return null;
        }
      }
      else if (HasArrangeVerbAncestor(invocation))
      {
        // Standalone arrange: m.Method(...).Returns/Throws/Does(...). Trust the arrange verb and
        // require the method to belong to the receiver's type (or a base of it).
        if (!IsDerivedFrom(receiverType, containingType))
        {
          return null;
        }
      }
      else
      {
        // When(...)/Received(...) without an explicit mock: can't prove the receiver is a mock.
        return null;
      }

      return Diagnostic.Create(
        Mock002,
        invocation.GetLocation(),
        containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        method.Name);
    }

    /// <summary>
    /// True when the receiver of <paramref name="invocation"/> is the parameter of the directly
    /// enclosing <c>A&lt;T&gt;(...)</c> / <c>Setup(m, ...)</c> arrange lambda, i.e. provably the mock.
    /// </summary>
    private static bool ReceiverIsArrangeLambdaParameter(GeneratorSyntaxContext ctx, InvocationExpressionSyntax invocation, System.Threading.CancellationToken ct)
    {
      if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
      {
        return false;
      }

      for (var current = invocation.Parent; current is not null; current = current.Parent)
      {
        if (current is AnonymousFunctionExpressionSyntax lambda)
        {
          if (!IsArrangeLambda(lambda))
          {
            return false;
          }

          var parameter = lambda switch
          {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
            _ => null,
          };

          if (parameter is null)
          {
            return false;
          }

          return ExpressionResolvesToParameter(ctx, memberAccess.Expression, ctx.SemanticModel.GetDeclaredSymbol(parameter, ct), ct, depth: 0);
        }
      }

      return false;
    }

    /// <summary>
    /// True when <paramref name="expression"/> is the arrange lambda parameter, possibly reached
    /// through a cast (<c>((T)x).M()</c>) or a local alias whose initializer is the parameter
    /// (<c>var y = x; y.M()</c>). This keeps MOCK002 diagnosing those forms while still ignoring a
    /// local that is a genuine real object (<c>RealImpl real = new(); real.M()</c>).
    /// </summary>
    private static bool ExpressionResolvesToParameter(GeneratorSyntaxContext ctx, ExpressionSyntax expression, ISymbol? parameter, System.Threading.CancellationToken ct, int depth)
    {
      if (parameter is null || depth > 8)
      {
        return false;
      }

      // Peel parentheses and casts: the cast target is the mocked type, the inner expression is
      // what carries the receiver's identity.
      while (true)
      {
        switch (expression)
        {
          case ParenthesizedExpressionSyntax parenthesized:
            expression = parenthesized.Expression;
            continue;
          case CastExpressionSyntax cast:
            expression = cast.Expression;
            continue;
          default:
            break;
        }

        break;
      }

      var symbol = ctx.SemanticModel.GetSymbolInfo(expression, ct).Symbol;
      if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
      {
        return true;
      }

      // Local alias: follow its initializer, but only when the local is not later reassigned from
      // a non-parameter source. If it is, the value at the use site is not provably the mock, so
      // suppress (a false negative beats a false MOCK002 error on a real object).
      if (symbol is ILocalSymbol local)
      {
        if (IsReassignedFromNonParameter(ctx, local, parameter, expression, ct, depth))
        {
          return false;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
          if (reference.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } initializer })
          {
            return ExpressionResolvesToParameter(ctx, initializer, parameter, ct, depth + 1);
          }
        }
      }

      return false;
    }

    /// <summary>
    /// True when <paramref name="local"/> has a simple assignment (beyond its declaration
    /// initializer) whose right-hand side does not resolve to the arrange parameter. In that case
    /// the local's value at <paramref name="useSite"/> cannot be proven to be the mock.
    /// </summary>
    private static bool IsReassignedFromNonParameter(
      GeneratorSyntaxContext ctx,
      ILocalSymbol local,
      ISymbol? parameter,
      SyntaxNode useSite,
      System.Threading.CancellationToken ct,
      int depth)
    {
      if (depth > 8)
      {
        return false;
      }

      // Bound the search to the nearest enclosing lambda/method/local function: an assignment
      // outside it cannot change the value observed at this use site.
      SyntaxNode? scope = useSite;
      while (scope is not null
             && scope is not (AnonymousFunctionExpressionSyntax or BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax))
      {
        scope = scope.Parent;
      }

      if (scope is null)
      {
        return false;
      }

      foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
      {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
          continue;
        }

        if (!SymbolEqualityComparer.Default.Equals(ctx.SemanticModel.GetSymbolInfo(assignment.Left, ct).Symbol, local))
        {
          continue;
        }

        if (!ExpressionResolvesToParameter(ctx, assignment.Right, parameter, ct, depth + 1))
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>The mocked type of the nearest enclosing A&lt;T&gt;(...)/Setup&lt;T&gt;(...)/InOrder&lt;T&gt;(...) call, if any.</summary>
    private static INamedTypeSymbol? GetEnclosingMockType(GeneratorSyntaxContext ctx, SyntaxNode node, System.Threading.CancellationToken ct)
    {
      for (var current = node.Parent; current is not null; current = current.Parent)
      {
        if (current is AnonymousFunctionExpressionSyntax lambda && !IsArrangeLambda(lambda))
        {
          return null;
        }

        if (current is InvocationExpressionSyntax invocation && CalleeName(invocation) is "A" or "Setup" or "InOrder")
        {
          var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ct);
          var method = symbolInfo.Symbol as IMethodSymbol;
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

          if (method is not null
              && method.ContainingType?.Name == "Mock"
              && method.ContainingType.ContainingNamespace is { Name: "Mocking", ContainingNamespace.Name: "Assertive" }
              && method.TypeArguments.Length == 1
              && method.TypeArguments[0] is INamedTypeSymbol target)
          {
            return target;
          }

          // Fall back to the explicit type argument syntax (A<T>(...) always names T) so an
          // unresolved lambda body doesn't hide the diagnostic.
          if (invocation.Expression is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic)
          {
            return ctx.SemanticModel.GetTypeInfo(generic.TypeArgumentList.Arguments[0], ct).Type as INamedTypeSymbol;
          }

          return null;
        }
      }

      return null;
    }

    /// <summary>True when a <c>.Returns</c>/<c>.Throws</c>/<c>.Does</c> ancestor appears before any arrange lambda.</summary>
    private static bool HasArrangeVerbAncestor(SyntaxNode node)
    {
      for (var current = node.Parent; current is not null; current = current.Parent)
      {
        if (current is AnonymousFunctionExpressionSyntax)
        {
          return false;
        }

        if (current is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Returns" or "Throws" or "Does" or "ReturnsSequentially" })
        {
          return true;
        }

        if (current is InvocationExpressionSyntax invocation && CalleeName(invocation) is "A" or "Setup" or "When" or "Received" or "DidNotReceive")
        {
          return false;
        }
      }

      return false;
    }

    private static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
      for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
      {
        if (SymbolEqualityComparer.Default.Equals(current, baseType))
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// MOCK005 (warning): a mocked class implements interface members explicitly. Those members are
    /// private and cannot be overridden by the generated subclass, so they run the real code and
    /// cannot be arranged.
    /// </summary>
    private static Diagnostic? ExtractMock005(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
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

      if (method is null
          || method.Name != "A"
          || method.ContainingType?.Name != "Mock"
          || method.ContainingType.ContainingNamespace is not { Name: "Mocking", ContainingNamespace.Name: "Assertive" }
          || method.TypeArguments.Length != 1
          || method.TypeArguments[0] is not INamedTypeSymbol target
          || target.TypeKind != TypeKind.Class
          || !IsMockableType(target))
      {
        return null;
      }

      var explicitMembers = GetExplicitInterfaceImplementations(target);
      if (explicitMembers.Count == 0)
      {
        return null;
      }

      return Diagnostic.Create(
        Mock005,
        invocation.GetLocation(),
        target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        string.Join(", ", explicitMembers));
    }

    private static List<string> GetExplicitInterfaceImplementations(INamedTypeSymbol type)
    {
      var result = new List<string>();

      for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
      {
        foreach (var member in current.GetMembers())
        {
          if (member is IMethodSymbol { MethodKind: MethodKind.ExplicitInterfaceImplementation } m)
          {
            foreach (var ifaceMethod in m.ExplicitInterfaceImplementations)
            {
              result.Add($"{ifaceMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{ifaceMethod.Name}");
            }
          }
          else if (member is IPropertySymbol p && !p.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
          {
            foreach (var ifaceProperty in p.ExplicitInterfaceImplementations)
            {
              result.Add($"{ifaceProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{(ifaceProperty.IsIndexer ? "this[]" : ifaceProperty.Name)}");
            }
          }
          else if (member is IEventSymbol e && !e.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
          {
            foreach (var ifaceEvent in e.ExplicitInterfaceImplementations)
            {
              result.Add($"{ifaceEvent.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{ifaceEvent.Name}");
            }
          }
        }
      }

      return result.Distinct().ToList();
    }

    /// <summary>
    /// MOCK003: warns when an arrangement targets a member the generator intentionally skips — a
    /// generic method, a ref-returning method, or a method with ref/out/in parameters that is being
    /// matched with argument matchers. Such arrangements would silently do nothing.
    /// Only fires for methods on interface or non-sealed class types (the kinds that get mocked),
    /// to avoid false positives on DSL/framework helpers (Any, ArrangeExtensions.Returns, etc.).
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
      else if (method.Parameters.Any(p => p.RefKind is RefKind.Ref or RefKind.Out or RefKind.In)
               && invocation.ArgumentList.Arguments.Any(a => Classify(a.Expression) != ArgKind.Exact))
      {
        // ref/out/in methods can be arranged with exact args + ReturnsWithOuts/SetsOuts, but the
        // generator never emits a matcher interceptor for them, so matchers would silently no-op.
        reason = "it has ref/out/in parameters, which cannot be combined with argument matchers";
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
    /// MOCK004: errors when named arguments are used at a matcher call site inside an arrange
    /// context. Named args desync the positional matcher queue, so they are not supported; the
    /// matcher would otherwise bind the wrong argument silently.
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

    private static string GetAccessModifier(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
      Accessibility.Protected => "protected ",
      Accessibility.ProtectedOrInternal => "protected internal ",
      _ => "public ",
    };

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
      if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax } inv)
      {
        return false;
      }

      if (inv.ArgumentList.Arguments.Any(static a =>
          UnwrapNullForgiving(a.Expression).IsKind(SyntaxKind.DefaultLiteralExpression)
          || IsMatcherCall(UnwrapNullForgiving(a.Expression))))
      {
        return true;
      }

      // Standalone arrangements with exact arguments (`mock.Method(x).Returns(v)`) are intercepted
      // too: it lets a strict mock be arranged with exact args (the probe must not trip the strict
      // check), while the interceptor still falls through for non-mock receivers.
      return HasArrangeVerbAncestor(inv);
    }

    /// <summary>
    /// True when <paramref name="node"/> is the fluent call being arranged standalone, e.g.
    /// <c>mock.Method(args).Returns(...)</c>, and is not inside an <c>A&lt;T&gt;(...)</c> / <c>Setup</c>
    /// arrange lambda (those are captured by the runtime capture mode instead).
    /// </summary>
    private static bool IsStandaloneArrangeProbe(SyntaxNode node)
    {
      // The call must be the direct receiver of a fluent arrange verb: `node.Returns(...)`.
      // A mock call merely nested inside the verb's arguments (e.g. `x.F(Any<string>(), other.Count())`)
      // is not itself being arranged and must run normally, not be swallowed by an interceptor.
      if (node is not InvocationExpressionSyntax invocation
          || invocation.Parent is not MemberAccessExpressionSyntax
          {
            Name.Identifier.ValueText: "Returns" or "Throws" or "Does" or "ReturnsSequentially",
          } verbAccess
          || !ReferenceEquals(verbAccess.Expression, invocation))
      {
        return false;
      }

      for (var current = node.Parent; current is not null; current = current.Parent)
      {
        if (current is AnonymousFunctionExpressionSyntax lambda && IsArrangeLambda(lambda))
        {
          return false;
        }

        if (current is InvocationExpressionSyntax enclosing
            && CalleeName(enclosing) is "When" or "Received" or "DidNotReceive" or "A" or "Setup")
        {
          return false;
        }
      }

      return true;
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

      // Static/extension/struct members can never be mock members — intercepting them would only
      // replace a real call. (Instance calls on real objects are handled at runtime by the
      // generated interceptor's IMockObject guard.)
      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
          || method.IsGenericMethod
          || method.IsStatic
          || method.IsExtensionMethod
          || method.Parameters.Any(p => p.RefKind != RefKind.None)
          || method.ContainingType is not { } containing
          || containing.TypeKind is not (TypeKind.Class or TypeKind.Interface))
      {
        return null;
      }

      var args = invocation.ArgumentList.Arguments;

      // Named arguments desync the positional matcher queue (MOCK004 warns); don't intercept.
      if (args.Any(a => a.NameColon != null))
      {
        return null;
      }

      // Map the call's arguments onto the declared parameters. Optional parameters that were
      // omitted become OptionalDefault, and an expanded params array is matched element-wise.
      var kinds = new ArgKind[method.Parameters.Length];
      var paramsElementKinds = new ImmutableArray<ArgKind>[method.Parameters.Length];
      var argIndex = 0;
      var hasMatcher = false;

      for (var pi = 0; pi < method.Parameters.Length; pi++)
      {
        var parameter = method.Parameters[pi];

        if (parameter.IsParams && args.Count != method.Parameters.Length)
        {
          var elements = ImmutableArray.CreateBuilder<ArgKind>();
          while (argIndex < args.Count)
          {
            var elementKind = Classify(args[argIndex].Expression);
            if (elementKind != ArgKind.Exact)
            {
              hasMatcher = true;
            }

            elements.Add(elementKind);
            argIndex++;
          }

          kinds[pi] = ArgKind.ParamsExpanded;
          paramsElementKinds[pi] = elements.ToImmutable();
        }
        else if (argIndex < args.Count)
        {
          var kind = Classify(args[argIndex].Expression);
          if (kind != ArgKind.Exact)
          {
            hasMatcher = true;
          }

          kinds[pi] = kind;
          argIndex++;
        }
        else
        {
          kinds[pi] = ArgKind.OptionalDefault;
        }
      }

      // Nothing to do unless at least one argument is a matcher, or this is a standalone exact-arg
      // arrangement that must be captured without running the strict check.
      if (!hasMatcher && !IsStandaloneArrangeProbe(invocation))
      {
        return null;
      }

      var location = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);

      if (location is null)
      {
        return null;
      }

      var parameters = method.Parameters.Select(p =>
      {
        var typeFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // typeof(...) can't take reference-type nullable annotations; value-type ? is preserved.
        var typeofFqn = p.Type.IsValueType ? typeFqn : typeFqn.Replace("?", "");
        var defaultLiteral = !p.IsParams && p.IsOptional ? FormatDefaultLiteral(p, typeFqn) : null;
        return new CallParameter(typeFqn, typeofFqn, p.Name, p.IsParams, defaultLiteral);
      }).ToImmutableArray();

      return new MatcherCall(
        method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        method.Name,
        method.ReturnsVoid ? null : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        parameters,
        kinds.ToImmutableArray(),
        paramsElementKinds.ToImmutableArray(),
        location.Version,
        location.Data,
        location.GetDisplayLocation());
    }

    /// <summary>Formats an omitted optional parameter's default value as C# source.</summary>
    private static string FormatDefaultLiteral(IParameterSymbol parameter, string typeFqn)
    {
      if (!parameter.HasExplicitDefaultValue || parameter.ExplicitDefaultValue is null)
      {
        return $"default({typeFqn})";
      }

      var value = parameter.ExplicitDefaultValue;
      return parameter.Type.TypeKind == TypeKind.Enum
        ? $"({typeFqn})({FormatConstant(value)})"
        : FormatConstant(value);
    }

    private static string FormatConstant(object value) => value switch
    {
      bool b => b ? "true" : "false",
      string s => SymbolDisplay.FormatLiteral(s, true),
      char c => SymbolDisplay.FormatLiteral(c, true),
      float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
      double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
      decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
      long l => l.ToString(CultureInfo.InvariantCulture) + "L",
      ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
      uint ui => ui.ToString(CultureInfo.InvariantCulture) + "U",
      _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default",
    };

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
        // A nested lambda (matcher predicate, Does callback, user helper) is evaluated later and is
        // not part of the arrangement itself, so calls inside it are not arrange calls.
        if (current is AnonymousFunctionExpressionSyntax lambda && !IsArrangeLambda(lambda))
        {
          return false;
        }

        // Inside an A<T>(...) / When(...) / Received(...) / DidNotReceive(...) / Setup(...) lambda.
        if (current is InvocationExpressionSyntax invocation && CalleeName(invocation) is "A" or "When" or "Received" or "DidNotReceive" or "Setup")
        {
          return true;
        }

        // Standalone arrange: mock.Method(Any<T>()).Returns(v) — the mock call (or an argument
        // inside it) is the receiver of a trailing arrange verb.
        if (current is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Returns" or "Throws" or "Does" or "ReturnsSequentially" })
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>True when <paramref name="lambda"/> is the arrange lambda passed directly to a mocking API.</summary>
    private static bool IsArrangeLambda(AnonymousFunctionExpressionSyntax lambda)
    {
      SyntaxNode? current = lambda.Parent;
      while (current is ParenthesizedExpressionSyntax or CastExpressionSyntax)
      {
        current = current.Parent;
      }

      return current is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }
        && CalleeName(invocation) is "A" or "When" or "Received" or "DidNotReceive" or "Setup";
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
    private static bool IsMockableType(INamedTypeSymbol type) => GetUnmockableReason(type) is null;

    /// <summary>Why <paramref name="type"/> cannot be mocked, or null if it can.</summary>
    private static string? GetUnmockableReason(INamedTypeSymbol type)
    {
      // Open generics (any unbound type parameter anywhere in the argument tree) cannot be mocked.
      if (type.IsGenericType && type.TypeArguments.Any(HasOpenTypeArgument))
      {
        return "it is an open generic type (only closed generic interfaces are supported)";
      }

      if (type.TypeKind == TypeKind.Interface)
      {
        // Generic-math interfaces (and anything else with static abstract members) cannot be
        // implemented by a generated class, so reject them up front instead of emitting bad code.
        return HasStaticAbstractMember(type) ? "it has static abstract members" : null;
      }

      if (type.TypeKind != TypeKind.Class)
      {
        // Structs/enums/static/sealed etc.
        if (type.TypeKind is TypeKind.Struct or TypeKind.Enum) return "it is a struct/value type";
        if (type.IsStatic) return "it is static";
        if (type.IsSealed) return "it is sealed";
        return "it cannot be mocked (only non-sealed classes and interfaces are supported)";
      }

      // Class-specific rejections.
      if (type.IsStatic) return "it is static";
      if (type.IsSealed) return "it is sealed";
      if (type.IsRecord) return "records cannot be mocked";
      if (!HasAccessibleConstructor(type)) return "it has no accessible constructor";
      return null;
    }

    private static bool HasStaticAbstractMember(INamedTypeSymbol type)
    {
      if (ContainsStaticAbstract(type.GetMembers()))
      {
        return true;
      }

      foreach (var iface in type.AllInterfaces)
      {
        if (ContainsStaticAbstract(iface.GetMembers()))
        {
          return true;
        }
      }

      return false;

      static bool ContainsStaticAbstract(System.Collections.Immutable.ImmutableArray<ISymbol> members) =>
        members.Any(m => m.IsStatic && m.IsAbstract);
    }

    private static bool HasAccessibleConstructor(INamedTypeSymbol type) =>
      type.InstanceConstructors.Any(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal);

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
        if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } m && !m.IsGenericMethod && !m.ReturnsByRef)
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
              m.Parameters.Select(p => (
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                p.Name,
                p.RefKind switch { RefKind.Out => "out ", RefKind.Ref => "ref ", RefKind.In => "in ", _ => "" }
              )).ToArray(),
              kind, innerFqn, innerMock, directMock, callBase, GetAccessModifier(m), elementFqn));
          }
        }
        else if (!isClass && member is IMethodSymbol { MethodKind: MethodKind.Ordinary } sm
                 && (sm.IsGenericMethod || sm.ReturnsByRef))
        {
          // Interface methods that can't be intercepted (generic, ref-return, or ref/out params) must still be implemented.
          var typeParamSuffix = sm.IsGenericMethod ? $"<{string.Join(", ", sm.TypeParameters.Select(tp => tp.Name))}>" : "";
          var constraintClauses = sm.IsGenericMethod ? BuildConstraintClauses(sm.TypeParameters) : "";
          var returnTypeFqn = sm.ReturnsVoid ? "void" : sm.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          var paramList = string.Join(", ", sm.Parameters.Select(p =>
          {
            var modifier = p.RefKind switch { RefKind.Out => "out ", RefKind.Ref => "ref ", RefKind.In => "in ", _ => "" };
            return $"{modifier}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}";
          }));
          var notSupportedMsg = "Assertive.Mocking: generic and ref-return methods cannot be arranged on source-generated mocks.";
          // Assign out params before throwing to satisfy definite-assignment; throw keeps it simple.
          var outAssignments = sm.Parameters.Where(p => p.RefKind == RefKind.Out)
            .Select(p => $" {p.Name} = default!;");
          // A matcher on a generic call is never intercepted; drop it before throwing so it cannot
          // bind the next predicate arrangement to the wrong call.
          var body = outAssignments.Any()
            ? $"{{ {string.Concat(outAssignments)} global::Assertive.Mocking.Mock.ClearPendingMatchers(); throw new global::System.NotSupportedException(\"{notSupportedMsg}\"); }}"
            : $"{{ global::Assertive.Mocking.Mock.ClearPendingMatchers(); throw new global::System.NotSupportedException(\"{notSupportedMsg}\"); }}";
          genericStubs.Add($"    public {returnTypeFqn} {sm.Name}{typeParamSuffix}({paramList}){constraintClauses} {body}");
        }
        else if (isClass && member is IMethodSymbol { MethodKind: MethodKind.Ordinary } am && am.IsAbstract && (am.IsGenericMethod || am.ReturnsByRef))
        {
          // Class abstract methods that can't be intercepted (generic/ref-return) must still be
          // overridden so the generated subclass compiles.
          var typeParamSuffix = am.IsGenericMethod ? $"<{string.Join(", ", am.TypeParameters.Select(tp => tp.Name))}>" : "";
          var constraintClauses = am.IsGenericMethod ? BuildConstraintClauses(am.TypeParameters) : "";
          var refPrefix = am.ReturnsByRef ? "ref " : "";
          var returnTypeFqn = am.ReturnsVoid ? "void" : am.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          var paramList = string.Join(", ", am.Parameters.Select(p =>
          {
            var modifier = p.RefKind switch { RefKind.Out => "out ", RefKind.Ref => "ref ", RefKind.In => "in ", _ => "" };
            return $"{modifier}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}";
          }));
          var outAssignments = am.Parameters.Where(p => p.RefKind == RefKind.Out)
            .Select(p => $" {p.Name} = default!;");
          var body = outAssignments.Any()
            ? $"{{ {string.Concat(outAssignments)} global::Assertive.Mocking.Mock.ClearPendingMatchers(); throw new global::System.NotSupportedException(\"Assertive.Mocking: generic and ref-return methods cannot be arranged on source-generated mocks.\"); }}"
            : "{ global::Assertive.Mocking.Mock.ClearPendingMatchers(); throw new global::System.NotSupportedException(\"Assertive.Mocking: generic and ref-return methods cannot be arranged on source-generated mocks.\"); }";
          genericStubs.Add($"    {GetAccessModifier(am)}override {refPrefix}{returnTypeFqn} {am.Name}{typeParamSuffix}({paramList}){constraintClauses} {body}");
        }
        else if (member is IPropertySymbol { IsIndexer: false } p)
        {
          var propCallBase = isClass && !p.IsAbstract;
          var hasSetter = p.SetMethod is not null
            && p.SetMethod.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
          var isInitOnly = p.SetMethod?.IsInitOnly == true;
          properties.Add(new MockProperty(p.Name, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), propCallBase, hasSetter, GetAccessModifier(p), isInitOnly));
        }
        else if (member is IPropertySymbol { IsIndexer: true } idx)
        {
          var idxCallBase = isClass && !idx.IsAbstract;
          indexers.Add(new MockIndexer(
            idx.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            idx.Parameters.Select(pp => (pp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), pp.Name)).ToArray(),
            idx.GetMethod is not null,
            idx.SetMethod is not null,
            idxCallBase,
            GetAccessModifier(idx)));
        }
        else if (member is IEventSymbol e)
        {
          var handlerFqn = e.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
          events.Add(new MockEvent(e.Name, handlerFqn, GetAccessModifier(e)));
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
        genericStubs.ToImmutableArray(),
        isClass && HasRequiredMembers(type));
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
      for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
      {
        foreach (var member in current.GetMembers())
        {
          if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
          {
            return true;
          }
        }
      }

      return false;
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
            // Overrides report IsVirtual == false when sealed, so accept IsOverride explicitly.
            if (!(m.IsVirtual || m.IsAbstract || m.IsOverride) || !IsOverridableAccessibility(m))
              continue;

            // Key by name + parameter types (not just arity) so same-arity overloads are all emitted.
            var methodKey = MethodKey(m);
            if (m.IsSealed)
            {
              // A sealed override cannot be intercepted; remember it so the base virtual isn't emitted.
              seen.Add(methodKey);
              continue;
            }

            if (seen.Add(methodKey))
              yield return m;
          }
          else if (member is IPropertySymbol { IsIndexer: false } p)
          {
            if (!(p.IsVirtual || p.IsAbstract || p.IsOverride) || !IsOverridableAccessibility(p))
              continue;
            if (p.IsSealed)
            {
              seen.Add($"prop:{p.Name}");
              continue;
            }

            if (seen.Add($"prop:{p.Name}"))
              yield return p;
          }
          else if (member is IPropertySymbol { IsIndexer: true } idx)
          {
            if (!(idx.IsVirtual || idx.IsAbstract || idx.IsOverride) || !IsOverridableAccessibility(idx))
              continue;
            var idxKey = $"indexer:{string.Join(",", idx.Parameters.Select(pp => pp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))}";
            if (idx.IsSealed)
            {
              seen.Add(idxKey);
              continue;
            }

            if (seen.Add(idxKey))
              yield return idx;
          }
          else if (member is IEventSymbol e)
          {
            if (!(e.IsVirtual || e.IsAbstract || e.IsOverride) || !IsOverridableAccessibility(e))
              continue;
            if (e.IsSealed)
            {
              seen.Add($"event:{e.Name}");
              continue;
            }

            if (seen.Add($"event:{e.Name}"))
              yield return e;
          }
        }
      }
    }

    /// <summary>Stable key for a method: name + parameter type FQNs, so overloads stay distinct.</summary>
    private static string MethodKey(IMethodSymbol method) =>
      method.Name + "|" + string.Join("|", method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

    private static bool IsOverridableAccessibility(ISymbol symbol)
      => symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

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
      if (type.IsSealed)
      {
        return false;
      }

      // Records and static-abstract interfaces cannot be implemented by a generated class.
      if (type.IsRecord)
      {
        return false;
      }

      if (type.TypeKind == TypeKind.Interface && HasStaticAbstractMember(type))
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

      sb.AppendLine($"  internal sealed class {wrapName} : global::Assertive.Mocking.Runtime.MockBase, {target.InterfaceFqn}");
      sb.AppendLine("  {");
      sb.AppendLine($"    private readonly {target.InterfaceFqn} __wrapped;");
      sb.AppendLine($"    public {wrapName}({target.InterfaceFqn} __wrapped) : base(\"{target.DisplayName}\")");
      sb.AppendLine("      => this.__wrapped = __wrapped;");

      foreach (var method in target.Methods)
      {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Mod}{p.Type} {p.Name}"));
        var wrappedCallArgs = string.Join(", ", method.Parameters.Select(p => $"{p.Mod}{p.Name}"));
        var inArgs = method.Parameters.Where(p => p.Mod != "out ").Select(p => p.Name).ToArray();
        var argsArray = inArgs.Length == 0
          ? "global::System.Array.Empty<object>()"
          : $"new object[] {{ {string.Join(", ", inArgs)} }}";
        var outParams = method.Parameters.Where(p => p.Mod == "out ").ToArray();
        var outRefParams = method.Parameters.Where(p => p.Mod is "out " or "ref ").ToArray();
        var hasOutParams = outRefParams.Length > 0;
        var outDefaults = outParams.Length > 0
          ? string.Join(" ", outParams.Select(p => $"{p.Name} = default!;"))
          : "";

        if (method.ReturnKind == ReturnKind.Void)
        {
          sb.AppendLine($"    public void {method.Name}({parameters})");
          sb.AppendLine("    {");
          if (hasOutParams)
          {
            sb.AppendLine($"      var __args = {argsArray};");
            var captureReturn = outDefaults.Length > 0 ? $"{{ {outDefaults} return; }}" : "return;";
            sb.AppendLine($"      if (OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) {captureReturn}");
            sb.AppendLine("      if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            sb.AppendLine("      if (__matched && __r is global::Assertive.Mocking.Runtime.OutResult __out)");
            sb.AppendLine("      {");
            for (var oi = 0; oi < outRefParams.Length; oi++)
              sb.AppendLine($"        {outRefParams[oi].Name} = __out.OutValues.Length > {oi} ? ({outRefParams[oi].Type})__out.OutValues[{oi}]! : default!;");
            sb.AppendLine("        return;");
            sb.AppendLine("      }");
            sb.AppendLine($"      if (!__matched) __wrapped.{method.Name}({wrappedCallArgs});");
            if (outDefaults.Length > 0)
              sb.AppendLine($"      else {{ {outDefaults} }}");
          }
          else
          {
            sb.AppendLine($"      if (OnCall(\"{method.Name}\", {argsArray}, out var __r, out var __matched)) return;");
            sb.AppendLine("      if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            sb.AppendLine($"      if (!__matched) __wrapped.{method.Name}({wrappedCallArgs});");
          }
          sb.AppendLine("    }");
        }
        else
        {
          sb.AppendLine($"    public {method.ReturnTypeFqn} {method.Name}({parameters})");
          sb.AppendLine("    {");
          sb.AppendLine($"      var __args = {argsArray};");
          if (hasOutParams)
          {
            var captureReturn = outDefaults.Length > 0 ? $"{{ {outDefaults} return default; }}" : "return default;";
            sb.AppendLine($"      if (OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) {captureReturn}");
            sb.AppendLine("      if (__matched)");
            sb.AppendLine("      {");
            var faultRetStr = FaultReturn(method);
            if (outDefaults.Length > 0 && faultRetStr.StartsWith("return ", StringComparison.Ordinal))
              sb.AppendLine($"        if (__r is global::Assertive.Mocking.Runtime.MockFault __f) {{ {outDefaults} {faultRetStr} }}");
            else
              sb.AppendLine($"        if (__r is global::Assertive.Mocking.Runtime.MockFault __f) {faultRetStr}");
            sb.AppendLine("        if (__r is global::Assertive.Mocking.Runtime.OutResult __out)");
            sb.AppendLine("        {");
            for (var oi = 0; oi < outRefParams.Length; oi++)
              sb.AppendLine($"          {outRefParams[oi].Name} = __out.OutValues.Length > {oi} ? ({outRefParams[oi].Type})__out.OutValues[{oi}]! : default!;");
            var retTypePat = method.ReturnTypeFqn!.TrimEnd('?');
            sb.AppendLine($"          return __out.ReturnValue is {retTypePat} __rv ? __rv : default;");
            sb.AppendLine("        }");
            if (outDefaults.Length > 0)
              sb.AppendLine($"        {outDefaults}");
            sb.AppendLine($"        return {ReturnIsExpr(method.ReturnTypeFqn!, MatchedFallback(method))};");
            sb.AppendLine("      }");
            if (outDefaults.Length > 0)
              sb.AppendLine($"      {outDefaults}");
            sb.AppendLine($"      return __wrapped.{method.Name}({wrappedCallArgs});");
          }
          else
          {
            sb.AppendLine($"      if (OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) return default;");
            sb.AppendLine("      if (__matched)");
            sb.AppendLine("      {");
            sb.AppendLine($"        if (__r is global::Assertive.Mocking.Runtime.MockFault __f) {FaultReturn(method)}");
            sb.AppendLine($"        return {ReturnIsExpr(method.ReturnTypeFqn!, MatchedFallback(method))};");
            sb.AppendLine("      }");
            sb.AppendLine($"      return __wrapped.{method.Name}({wrappedCallArgs});");
          }
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
        sb.AppendLine($"          if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
        sb.AppendLine($"          return {ReturnIsExpr(property.TypeFqn, "default")};");
        sb.AppendLine("        }");
        sb.AppendLine($"        return __wrapped.{property.Name};");
        sb.AppendLine("      }");
        if (property.HasSetter)
        {
          if (property.IsInitOnly)
          {
            // init-only accessors cannot assign the wrapped instance's property; just record it.
            sb.AppendLine($"      init {{ OnCall(\"set_{property.Name}\", new object[] {{ value }}, out _, out _); }}");
          }
          else
          {
            sb.AppendLine("      set");
            sb.AppendLine("      {");
            sb.AppendLine($"        if (OnCall(\"set_{property.Name}\", new object[] {{ value }}, out var __r, out var __matched)) return;");
            sb.AppendLine("        if (__matched)");
            sb.AppendLine("        {");
            sb.AppendLine("          if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            sb.AppendLine("          return; // arranged setter: don't mutate the wrapped object");
            sb.AppendLine("        }");
            sb.AppendLine($"        __wrapped.{property.Name} = value;");
            sb.AppendLine("      }");
          }
        }
        sb.AppendLine("    }");
      }

      // Generic/ref-return interface members can't be intercepted but must still be implemented so
      // the wrapper class satisfies the interface (calling them throws NotSupportedException).
      foreach (var stub in target.GenericStubs)
        sb.AppendLine(stub);

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
          sb.AppendLine($"        if (__matched) {{ if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception; return {ReturnIsExpr(indexer.ReturnTypeFqn, "default")}; }}");
          sb.AppendLine($"        return __wrapped[{argNames}];");
          sb.AppendLine("      }");
        }
        if (indexer.HasSetter)
        {
          sb.AppendLine("      set");
          sb.AppendLine("      {");
          sb.AppendLine($"        if (OnCall(\"set_Item\", new object[] {{ {argNames}, value }}, out var __r, out var __matched)) return;");
          sb.AppendLine("        if (__matched)");
          sb.AppendLine("        {");
          sb.AppendLine("          if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
          sb.AppendLine("          return; // arranged setter: don't mutate the wrapped object");
          sb.AppendLine("        }");
          sb.AppendLine($"        __wrapped[{argNames}] = value;");
          sb.AppendLine("      }");
        }
        sb.AppendLine("    }");
      }

      foreach (var ev in target.Events)
      {
        sb.AppendLine($"    public event {ev.HandlerTypeFqn} {ev.Name}");
        sb.AppendLine("    {");
        sb.AppendLine($"      add {{ if (TryCaptureEvent(\"{ev.Name}\")) return; AddEventHandler(\"{ev.Name}\", value); __wrapped.{ev.Name} += value; }}");
        sb.AppendLine($"      remove {{ RemoveEventHandler(\"{ev.Name}\", value); __wrapped.{ev.Name} -= value; }}");
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

      // Resolve the declared type of each provided argument at the call site
      var args = invocation.ArgumentList.Arguments;
      var argTypes = args.Select(a => ctx.SemanticModel.GetTypeInfo(a.Expression, ct).Type).ToArray();
      // `null`/`default` literals have no type of their own; treat them as bindable to any
      // reference type or Nullable<T> so they're passed through instead of auto-mocked.
      var argIsNull = args.Select(a =>
        a.Expression.IsKind(SyntaxKind.NullLiteralExpression)
        || a.Expression.IsKind(SyntaxKind.DefaultLiteralExpression)).ToArray();

      bool CanBind(int argIndex, ITypeSymbol parameterType)
      {
        return argIsNull[argIndex]
          ? parameterType.IsReferenceType || IsNullableValueType(parameterType)
          : argTypes[argIndex] is { } argType && IsAssignableTo(argType, parameterType);
      }

      // Pick the best accessible constructor: prefer the one that uses the most provided arguments,
      // then the longest constructor among ties. A constructor is only viable when every unmatched
      // parameter can be auto-mocked or defaulted.
      var candidates = target.InstanceConstructors
        .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
        .ToList();

      IMethodSymbol? ctor = null;
      var bestMatched = -1;
      var bestLength = -1;

      foreach (var candidate in candidates)
      {
        var used = new HashSet<int>();
        var matched = 0;
        var viable = true;

        foreach (var param in candidate.Parameters)
        {
          var matchedIndex = -1;
          for (var i = 0; i < argTypes.Length; i++)
          {
            if (used.Contains(i)) continue;
            if (CanBind(i, param.Type))
            {
              matchedIndex = i;
              used.Add(i);
              break;
            }
          }

          if (matchedIndex >= 0)
          {
            matched++;
          }
          else if (param.Type is not INamedTypeSymbol named || !IsAutoMockable(named))
          {
            // Reference types with no matching argument and no auto-mock cannot be satisfied.
            if (!param.Type.IsValueType)
            {
              viable = false;
              break;
            }
          }
        }

        if (!viable) continue;

        if (matched > bestMatched || (matched == bestMatched && candidate.Parameters.Length > bestLength))
        {
          ctor = candidate;
          bestMatched = matched;
          bestLength = candidate.Parameters.Length;
        }
      }

      if (ctor is null) return null;

      // Match each constructor param to the first unmatched provided arg by type
      var usedArgIndices = new HashSet<int>();
      var buildParams = new List<BuildParam>();
      var autoMockedTypes = new List<INamedTypeSymbol>();

      foreach (var param in ctor.Parameters)
      {
        int matchedArgIndex = -1;
        for (int i = 0; i < argTypes.Length; i++)
        {
          if (usedArgIndices.Contains(i)) continue;
          if (CanBind(i, param.Type))
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

    private static bool IsNullableValueType(ITypeSymbol type) =>
      type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

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
          if (p.MockClassName != null) return $"({p.TypeFqn})global::Assertive.Mocking.Runtime.MockFactoryRegistry.Create<{p.TypeFqn}>(global::System.Array.Empty<object>())";
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
      var arrangeOverloadTargets = distinct.Concat(wraps.Select(w => w.Target)).ToList();
      if (HasArrangeOverloads(arrangeOverloadTargets))
      {
        sb.AppendLine("global using static global::Assertive.Mocking.Generated.MockArrange;");
      }
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
        sb.AppendLine($"      global::Assertive.Mocking.Runtime.MockFactoryRegistry.Register(typeof({target.InterfaceFqn}), {Factory(target)});");
      }

      foreach (var wrap in wraps)
      {
        sb.AppendLine($"      global::Assertive.Mocking.Runtime.WrapFactoryRegistry.Register(typeof({wrap.Target.InterfaceFqn}), __w => new {wrap.WrapClassName}(({wrap.Target.InterfaceFqn})__w));");
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

    /// <summary>Returns true when at least one non-generic <c>Any(methodGroup)</c> overload would be emitted.</summary>
    private static bool HasArrangeOverloads(List<MockTarget> targets)
    {
      var emitted = new HashSet<string>();
      foreach (var method in targets.SelectMany(t => t.Methods))
      {
        if (BuildOverload(method) is { } overload && emitted.Add(overload.DelegateType))
        {
          return true;
        }
      }
      return false;
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
    /// Emits an extension-method interceptor per matcher-bearing mock call. Each mirrors the
    /// original method's signature (including optional defaults and <c>params</c>), builds the
    /// per-argument matcher array plus the declared parameter types (for overload disambiguation),
    /// and captures them on the mock. When the receiver is not a mock, the interceptor calls the
    /// real method unchanged so non-mock calls inside arrange lambdas keep working.
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
        var parameterDecls = call.Parameters.Select(p =>
        {
          var modifier = p.IsParams ? "params " : "";
          var defaultSuffix = p.DefaultLiteral is null ? "" : $" = {p.DefaultLiteral}";
          return $"{modifier}{p.TypeFqn} {p.Name}{defaultSuffix}";
        }).ToList();
        var parameters = string.Join(", ", parameterDecls);
        // A parameterless call (e.g. a zero-arg standalone arrangement) has no trailing comma.
        var receiverSignature = parameterDecls.Count == 0
          ? $"this {call.ReceiverFqn} __r"
          : $"this {call.ReceiverFqn} __r, {parameters}";
        var callArgs = string.Join(", ", call.Parameters.Select(p => p.Name));
        var returnType = call.ReturnTypeFqn ?? "void";
        var displayArgs = string.Join(", ", call.Parameters.Select(p => $"(object){p.Name}"));
        var parameterTypes = string.Join(", ", call.Parameters.Select(p => $"typeof({p.TypeofFqn})"));

        var prelude = new List<string>();
        var matcherExprs = new List<string>();
        var predicateCount = 0;

        for (var pi = 0; pi < call.Parameters.Length; pi++)
        {
          var parameter = call.Parameters[pi];
          switch (call.ArgumentKinds[pi])
          {
            case ArgKind.Any:
              matcherExprs.Add("static (object __x) => true");
              break;
            case ArgKind.Predicate:
              predicateCount++;
              matcherExprs.Add("global::Assertive.Mocking.Mock.DequeueMatcher()");
              break;
            case ArgKind.ParamsExpanded:
              var elements = call.ParamsElementKinds[pi];
              var elementNames = new List<string>();
              for (var j = 0; j < elements.Length; j++)
              {
                var elementName = $"__p{pi}_{j}";
                elementNames.Add(elementName);
                if (elements[j] == ArgKind.Predicate)
                {
                  predicateCount++;
                }

                var elementExpr = elements[j] switch
                {
                  ArgKind.Any => "static (object __e) => true",
                  ArgKind.Predicate => "global::Assertive.Mocking.Mock.DequeueMatcher()",
                  _ => $"(object __e) => __m.ArgumentsMatchEqual(new object[] {{ __e }}, new object[] {{ {parameter.Name}[{j}] }})",
                };
                prelude.Add($"      global::System.Func<object, bool> {elementName} = {elementExpr};");
              }

              var conditions = new List<string> { $"__arr.Length == {elements.Length}" };
              for (var j = 0; j < elementNames.Count; j++)
              {
                conditions.Add($"{elementNames[j]}(__arr[{j}])");
              }

              matcherExprs.Add($"__x => __x is {parameter.TypeFqn} __arr && {string.Join(" && ", conditions)}");
              break;
            default:
              // Exact args (and omitted optionals, matched against their default) compare structurally.
              matcherExprs.Add($"(object __x) => __m.ArgumentsMatchEqual(new object[] {{ __x }}, new object[] {{ {parameter.Name} }})");
              break;
          }
        }

        sb.AppendLine($"    // {call.DisplayLocation}");
        sb.AppendLine($"    [global::System.Runtime.CompilerServices.InterceptsLocation({call.LocationVersion}, {SymbolDisplay.FormatLiteral(call.LocationData, true)})]");
        sb.AppendLine($"    public static {returnType} Match_{index}({receiverSignature})");
        sb.AppendLine("    {");
        sb.AppendLine("      if ((object)__r is global::Assertive.Mocking.Runtime.IMockObject __mock)");
        sb.AppendLine("      {");
        sb.AppendLine("        var __m = __mock.Core;");
        // A matcher helper whose call is not intercepted (a matcher stored in a local, or a call with
        // named arguments) leaves its predicate in the queue. This call's own matchers are the most
        // recently enqueued, so keep only those and drop any stale predicate ahead of them.
        if (predicateCount > 0)
        {
          sb.AppendLine($"        global::Assertive.Mocking.Mock.PrunePendingMatchers({predicateCount});");
        }

        foreach (var line in prelude)
        {
          sb.AppendLine(line);
        }

        sb.AppendLine($"        __m.CaptureMatchers(\"{call.Method}\", new global::System.Func<object, bool>[] {{ {string.Join(", ", matcherExprs)} }}, new global::System.Type[] {{ {parameterTypes} }}, new object[] {{ {displayArgs} }});");

        if (call.ReturnTypeFqn != null)
        {
          sb.AppendLine("        return default;");
        }
        else
        {
          sb.AppendLine("        return;");
        }

        sb.AppendLine("      }");
        // The receiver is not a mock, so this interceptor never consumed the argument matchers.
        // Drop them rather than leaving them to bind the next predicate arrangement.
        sb.AppendLine("      global::Assertive.Mocking.Mock.ClearPendingMatchers();");
        sb.AppendLine(call.ReturnTypeFqn != null
          ? $"      return __r.{call.Method}({callArgs});"
          : $"      __r.{call.Method}({callArgs});");
        sb.AppendLine("    }");
        index++;
      }

      sb.AppendLine("  }");
    }

    /// <summary>Maps a method to its (delegate type, builder type, explicit type args), or null for arities the runtime builders don't cover.</summary>
    private static (string DelegateType, string BuilderType, string TypeArgs)? BuildOverload(MockMethod method)
    {
      var ps = method.Parameters;

      // out/ref params can't be expressed as Action<T>/Func<T> delegates
      if (ps.Any(p => p.Mod != ""))
      {
        return null;
      }

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

    /// <summary>The registry factory: ignores args for interfaces; dispatches to a base constructor whose parameter types match the provided argument runtime types for classes.</summary>
    private static string Factory(MockTarget target)
    {
      if (!target.IsClass || target.Constructors.Length == 0)
      {
        return $"static __a => new {target.ClassName}()";
      }

      var body = new StringBuilder();
      body.AppendLine("static __a =>");
      body.AppendLine("      {");

      foreach (var ctor in target.Constructors)
      {
        var typeChecks = ctor.Select((t, i) => $"__a[{i}] is {t}").ToArray();
        var cast = string.Join(", ", ctor.Select((t, i) => $"({t})__a[{i}]!"));
        var condition = typeChecks.Length == 0
          ? $"__a.Length == {ctor.Length}"
          : $"__a.Length == {ctor.Length} && {string.Join(" && ", typeChecks)}";
        body.AppendLine($"        if ({condition}) return new {target.ClassName}({cast});");
      }

      body.AppendLine($"        throw new global::System.InvalidOperationException(\"Assertive.Mocking: no {target.DisplayName} constructor matches the provided argument types.\");");
      body.Append("      }");

      return body.ToString();
    }

    private static void EmitMockClass(StringBuilder sb, MockTarget target)
    {
      // Interface mocks inherit MockBase (the engine); class mocks must extend the mocked class,
      // so they compose a MockBase and reach it through IMockObject.
      var core = target.IsClass ? "__core." : "";

      sb.AppendLine(target.IsClass
        ? $"  internal sealed class {target.ClassName} : {target.InterfaceFqn}, global::Assertive.Mocking.Runtime.IMockObject"
        : $"  internal sealed class {target.ClassName} : global::Assertive.Mocking.Runtime.MockBase, {target.InterfaceFqn}");
      sb.AppendLine("  {");

      if (target.IsClass)
      {
        sb.AppendLine($"    private readonly global::Assertive.Mocking.Runtime.MockBase __core = new global::Assertive.Mocking.Runtime.MockBase(\"{target.DisplayName}\");");
        sb.AppendLine("    global::Assertive.Mocking.Runtime.MockBase global::Assertive.Mocking.Runtime.IMockObject.Core => __core;");

        foreach (var ctor in target.Constructors)
        {
          var ctorParams = string.Join(", ", ctor.Select((t, i) => $"{t} __c{i}"));
          var baseArgs = string.Join(", ", Enumerable.Range(0, ctor.Length).Select(i => $"__c{i}"));
          // A class with required members can't be constructed without an initializer unless the
          // ctor is marked SetsRequiredMembers; make the generated ctors satisfy that contract.
          var setsRequired = target.HasRequiredMembers ? "[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers] " : "";
          sb.AppendLine(ctor.Length == 0
            ? $"    {setsRequired}public {target.ClassName}() {{ }}"
            : $"    {setsRequired}public {target.ClassName}({ctorParams}) : base({baseArgs}) {{ }}");
        }
      }
      else
      {
        sb.AppendLine($"    public {target.ClassName}() : base(\"{target.DisplayName}\") {{ }}");
      }

      foreach (var method in target.Methods)
      {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Mod}{p.Type} {p.Name}"));
        var baseCallArgs = string.Join(", ", method.Parameters.Select(p => $"{p.Mod}{p.Name}"));

        // Out params have no input value; exclude from __args (used for call recording and matching).
        // Ref params do have input values and are included.
        var inArgs = method.Parameters.Where(p => p.Mod != "out ").Select(p => p.Name).ToArray();
        var argsArray = inArgs.Length == 0
          ? "global::System.Array.Empty<object>()"
          : $"new object[] {{ {string.Join(", ", inArgs)} }}";

        // Out params (not ref) require definite assignment before every return path.
        var outParams = method.Parameters.Where(p => p.Mod == "out ").ToArray();
        var outRefParams = method.Parameters.Where(p => p.Mod is "out " or "ref ").ToArray();
        // All out+ref params (but not in) are assigned from OutResult when an OutResult arrangement is matched.
        var hasOutParams = outRefParams.Length > 0;
        var outDefaults = outParams.Length > 0
          ? string.Join(" ", outParams.Select(p => $"{p.Name} = default!;"))
          : "";

        if (method.ReturnKind == ReturnKind.Void)
        {
          var methodModifier = target.IsClass ? $"{method.AccessModifier}override " : method.AccessModifier;
          sb.AppendLine($"    {methodModifier}void {method.Name}({parameters})");
          sb.AppendLine("    {");
          if (hasOutParams)
          {
            sb.AppendLine($"      var __args = {argsArray};");
            var captureReturn = outDefaults.Length > 0 ? $"{{ {outDefaults} return; }}" : "return;";
            sb.AppendLine($"      if ({core}OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) {captureReturn}");
            sb.AppendLine("      if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            sb.AppendLine("      if (__matched && __r is global::Assertive.Mocking.Runtime.OutResult __out)");
            sb.AppendLine("      {");
            for (var oi = 0; oi < outRefParams.Length; oi++)
              sb.AppendLine($"        {outRefParams[oi].Name} = __out.OutValues.Length > {oi} ? ({outRefParams[oi].Type})__out.OutValues[{oi}]! : default!;");
            sb.AppendLine("        return;");
            sb.AppendLine("      }");
            if (method.CallBase)
              sb.AppendLine($"      if (!__matched) base.{method.Name}({baseCallArgs});");
            if (outDefaults.Length > 0)
              sb.AppendLine($"      {outDefaults}");
          }
          else
          {
            sb.AppendLine($"      if ({core}OnCall(\"{method.Name}\", {argsArray}, out var __r, out var __matched)) return;");
            sb.AppendLine("      if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            if (method.CallBase)
              sb.AppendLine($"      if (!__matched) base.{method.Name}({baseCallArgs});");
          }
          sb.AppendLine("    }");
          continue;
        }

        var unarranged = method.CallBase ? $"base.{method.Name}({baseCallArgs})" : UnarrangedReturn(method, core);

        var returnMethodModifier = target.IsClass ? $"{method.AccessModifier}override " : method.AccessModifier;
        sb.AppendLine($"    {returnMethodModifier}{method.ReturnTypeFqn} {method.Name}({parameters})");
        sb.AppendLine("    {");
        sb.AppendLine($"      var __args = {argsArray};");
        if (hasOutParams)
        {
          var captureReturn = outDefaults.Length > 0 ? $"{{ {outDefaults} return default; }}" : "return default;";
          sb.AppendLine($"      if ({core}OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) {captureReturn}");
          sb.AppendLine("      if (__matched)");
          sb.AppendLine("      {");
          // FaultReturn may be a throw (out params don't need assignment) or a return (they do).
          var faultRetStr = FaultReturn(method);
          if (outDefaults.Length > 0 && faultRetStr.StartsWith("return ", StringComparison.Ordinal))
            sb.AppendLine($"        if (__r is global::Assertive.Mocking.Runtime.MockFault __f) {{ {outDefaults} {faultRetStr} }}");
          else
            sb.AppendLine($"        if (__r is global::Assertive.Mocking.Runtime.MockFault __f) {faultRetStr}");
          sb.AppendLine("        if (__r is global::Assertive.Mocking.Runtime.OutResult __out)");
          sb.AppendLine("        {");
          for (var oi = 0; oi < outRefParams.Length; oi++)
            sb.AppendLine($"          {outRefParams[oi].Name} = __out.OutValues.Length > {oi} ? ({outRefParams[oi].Type})__out.OutValues[{oi}]! : default!;");
          var retTypePat = method.ReturnTypeFqn!.TrimEnd('?');
          sb.AppendLine($"          return __out.ReturnValue is {retTypePat} __rv ? __rv : default;");
          sb.AppendLine("        }");
          if (outDefaults.Length > 0)
            sb.AppendLine($"        {outDefaults}");
          sb.AppendLine($"        return {ReturnIsExpr(method.ReturnTypeFqn!, MatchedFallback(method))};");
          sb.AppendLine("      }");
          if (outDefaults.Length > 0)
            sb.AppendLine($"      {outDefaults}");
          sb.AppendLine($"      return {unarranged};");
        }
        else
        {
          sb.AppendLine($"      if ({core}OnCall(\"{method.Name}\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine("      if (__matched)");
          sb.AppendLine("      {");
          sb.AppendLine($"        if (__r is global::Assertive.Mocking.Runtime.MockFault __f) {FaultReturn(method)}");
          sb.AppendLine($"        return {ReturnIsExpr(method.ReturnTypeFqn!, MatchedFallback(method))};");
          sb.AppendLine("      }");
          sb.AppendLine($"      return {unarranged};");
        }
        sb.AppendLine("    }");
      }

      foreach (var property in target.Properties)
      {
        if (!target.IsClass)
        {
          // Interface properties: instrument the getter so arrangements and Received() work,
          // and record setter calls. The getter returns default when unarranged (no base to call).
          sb.AppendLine($"    {property.AccessModifier}{property.TypeFqn} {property.Name}");
          sb.AppendLine("    {");
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = global::System.Array.Empty<object>();");
          sb.AppendLine($"        if (OnCall(\"get_{property.Name}\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine("        if (__matched)");
          sb.AppendLine("        {");
          sb.AppendLine($"          if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
          sb.AppendLine($"          return {ReturnIsExpr(property.TypeFqn, "default")};");
          sb.AppendLine("        }");
          sb.AppendLine("        return default;");
          sb.AppendLine("      }");
          if (property.HasSetter)
          {
            sb.AppendLine($"      {(property.IsInitOnly ? "init" : "set")}");
            sb.AppendLine("      {");
            sb.AppendLine($"        if (OnCall(\"set_{property.Name}\", new object[] {{ value }}, out var __r, out var __matched)) return;");
            sb.AppendLine("        if (__matched && __r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            sb.AppendLine("      }");
          }
          sb.AppendLine("    }");
        }
        else
        {
          var unarrangedPropReturn = property.CallBase ? $"base.{property.Name}" : "default";
          sb.AppendLine($"    {property.AccessModifier}override {property.TypeFqn} {property.Name}");
          sb.AppendLine("    {");
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = global::System.Array.Empty<object>();");
          sb.AppendLine($"        if ({core}OnCall(\"get_{property.Name}\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine("        if (__matched)");
          sb.AppendLine("        {");
          sb.AppendLine($"          if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
          sb.AppendLine($"          return {ReturnIsExpr(property.TypeFqn, "default")};");
          sb.AppendLine("        }");
          sb.AppendLine($"        return {unarrangedPropReturn};");
          sb.AppendLine("      }");
          if (property.HasSetter)
          {
            sb.AppendLine($"      {(property.IsInitOnly ? "init" : "set")}");
            sb.AppendLine("      {");
            sb.AppendLine($"        if ({core}OnCall(\"set_{property.Name}\", new object[] {{ value }}, out var __r, out var __matched)) return;");
            sb.AppendLine("        if (__matched && __r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
            sb.AppendLine("      }");
          }
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
        var idxModifier = target.IsClass ? $"{indexer.AccessModifier}override " : indexer.AccessModifier;

        sb.AppendLine($"    {idxModifier}{indexer.ReturnTypeFqn} this[{parameters}]");
        sb.AppendLine("    {");
        if (indexer.HasGetter)
        {
          sb.AppendLine("      get");
          sb.AppendLine("      {");
          sb.AppendLine($"        var __args = {argsArray};");
          sb.AppendLine($"        if ({core}OnCall(\"get_Item\", __args, out var __r, out var __matched)) return default;");
          sb.AppendLine($"        if (__matched) {{ if (__r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception; return {ReturnIsExpr(indexer.ReturnTypeFqn, "default")}; }}");
          var unarrangedIdx = indexer.CallBase ? $"base[{argNames}]" : "default";
          sb.AppendLine($"        return {unarrangedIdx};");
          sb.AppendLine("      }");
        }
        if (indexer.HasSetter)
        {
          sb.AppendLine("      set");
          sb.AppendLine("      {");
          sb.AppendLine($"        if ({core}OnCall(\"set_Item\", new object[] {{ {argNames}, value }}, out var __r, out var __matched)) return;");
          sb.AppendLine("        if (__matched && __r is global::Assertive.Mocking.Runtime.MockFault __f) throw __f.Exception;");
          sb.AppendLine("      }");
        }
        sb.AppendLine("    }");
      }

      foreach (var ev in target.Events)
      {
        var evModifier = target.IsClass ? $"{ev.AccessModifier}override " : ev.AccessModifier;
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
    /// <summary>The optional parameter was omitted at the call site; match its declared default.</summary>
    OptionalDefault,
    /// <summary>A <c>params</c> array was expanded at the call site; elements are matched individually.</summary>
    ParamsExpanded,
  }

  /// <summary>One declared parameter of an intercepted call, used to mirror the method signature.</summary>
  internal readonly struct CallParameter : IEquatable<CallParameter>
  {
    public CallParameter(string typeFqn, string typeofFqn, string name, bool isParams, string? defaultLiteral)
    {
      TypeFqn = typeFqn;
      TypeofFqn = typeofFqn;
      Name = name;
      IsParams = isParams;
      DefaultLiteral = defaultLiteral;
    }

    /// <summary>Type as written in the interceptor signature.</summary>
    public string TypeFqn { get; }
    /// <summary>Type without reference-type nullable annotations, safe for <c>typeof(...)</c>.</summary>
    public string TypeofFqn { get; }
    public string Name { get; }
    public bool IsParams { get; }
    /// <summary>C# literal for an omitted optional's default, or null when not optional.</summary>
    public string? DefaultLiteral { get; }

    public bool Equals(CallParameter other) =>
      TypeFqn == other.TypeFqn && TypeofFqn == other.TypeofFqn && Name == other.Name && IsParams == other.IsParams && DefaultLiteral == other.DefaultLiteral;

    public override bool Equals(object? obj) => obj is CallParameter p && Equals(p);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = TypeFqn?.GetHashCode() ?? 0;
        h = h * 31 + (TypeofFqn?.GetHashCode() ?? 0);
        h = h * 31 + (Name?.GetHashCode() ?? 0);
        h = h * 31 + IsParams.GetHashCode();
        h = h * 31 + (DefaultLiteral?.GetHashCode() ?? 0);
        return h;
      }
    }
  }

  internal sealed class MatcherCall : IEquatable<MatcherCall>
  {
    public MatcherCall(string receiverFqn, string method, string? returnTypeFqn, ImmutableArray<CallParameter> parameters,
      ImmutableArray<ArgKind> argumentKinds, ImmutableArray<ImmutableArray<ArgKind>> paramsElementKinds,
      int locationVersion, string locationData, string displayLocation)
    {
      ReceiverFqn = receiverFqn;
      Method = method;
      ReturnTypeFqn = returnTypeFqn;
      Parameters = parameters;
      ArgumentKinds = argumentKinds;
      ParamsElementKinds = paramsElementKinds.IsDefault ? ImmutableArray<ImmutableArray<ArgKind>>.Empty : paramsElementKinds;
      LocationVersion = locationVersion;
      LocationData = locationData;
      DisplayLocation = displayLocation;
    }

    public string ReceiverFqn { get; }
    public string Method { get; }
    public string? ReturnTypeFqn { get; }
    public ImmutableArray<CallParameter> Parameters { get; }
    /// <summary>One entry per declared parameter.</summary>
    public ImmutableArray<ArgKind> ArgumentKinds { get; }
    /// <summary>Parallel to <see cref="ArgumentKinds"/>; element kinds when the kind is ParamsExpanded.</summary>
    public ImmutableArray<ImmutableArray<ArgKind>> ParamsElementKinds { get; }
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
      if (!Parameters.SequenceEqual(other.Parameters)) return false;
      if (!ArgumentKinds.SequenceEqual(other.ArgumentKinds)) return false;
      if (ParamsElementKinds.Length != other.ParamsElementKinds.Length) return false;
      for (var i = 0; i < ParamsElementKinds.Length; i++)
      {
        if (!ParamsElementKinds[i].SequenceEqual(other.ParamsElementKinds[i])) return false;
      }
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
        h = h * 31 + Parameters.Length;
        return h;
      }
    }
  }

  internal sealed class MockTarget : IEquatable<MockTarget>
  {
    public MockTarget(string interfaceFqn, string className, string displayName, bool isClass,
      ImmutableArray<MockMethod> methods, ImmutableArray<MockProperty> properties, ImmutableArray<ImmutableArray<string>> constructors,
      ImmutableArray<MockIndexer> indexers = default, ImmutableArray<MockEvent> events = default,
      ImmutableArray<string> genericStubs = default, bool hasRequiredMembers = false)
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
      HasRequiredMembers = hasRequiredMembers;
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

    /// <summary>True when the mocked class has required members, so generated ctors need SetsRequiredMembers.</summary>
    public bool HasRequiredMembers { get; }

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
      if (HasRequiredMembers != other.HasRequiredMembers) return false;
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
        h = h * 31 + HasRequiredMembers.GetHashCode();
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
    public MockMethod(string name, string? returnTypeFqn, (string Type, string Name, string Mod)[] parameters,
      ReturnKind returnKind, string? innerTypeFqn, string? innerMockClassName, string? directMockClassName, bool callBase,
      string accessModifier, string? elementTypeFqn = null)
    {
      Name = name;
      ReturnTypeFqn = returnTypeFqn;
      Parameters = parameters;
      ReturnKind = returnKind;
      InnerTypeFqn = innerTypeFqn;
      InnerMockClassName = innerMockClassName;
      DirectMockClassName = directMockClassName;
      CallBase = callBase;
      AccessModifier = accessModifier;
      ElementTypeFqn = elementTypeFqn;
    }

    /// <summary>True for a non-abstract virtual class member: unarranged calls run the real base method.</summary>
    public bool CallBase { get; }

    public string Name { get; }
    public string? ReturnTypeFqn { get; }
    public string AccessModifier { get; }
    /// <summary>Parameters: Type = FQN type, Name = parameter name, Mod = "" / "out " / "ref " / "in ".</summary>
    public (string Type, string Name, string Mod)[] Parameters { get; }
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
      if (AccessModifier != other.AccessModifier) return false;
      if (CallBase != other.CallBase) return false;
      if (Parameters.Length != other.Parameters.Length) return false;
      for (var i = 0; i < Parameters.Length; i++)
        if (Parameters[i].Type != other.Parameters[i].Type || Parameters[i].Name != other.Parameters[i].Name || Parameters[i].Mod != other.Parameters[i].Mod)
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
        h = h * 31 + (AccessModifier?.GetHashCode() ?? 0);
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
    public MockProperty(string name, string typeFqn, bool callBase = false, bool hasSetter = true, string accessModifier = "public ", bool isInitOnly = false)
    {
      Name = name;
      TypeFqn = typeFqn;
      CallBase = callBase;
      HasSetter = hasSetter;
      AccessModifier = accessModifier;
      IsInitOnly = isInitOnly;
    }

    public string Name { get; }
    public string TypeFqn { get; }
    public string AccessModifier { get; }
    public bool CallBase { get; }
    public bool HasSetter { get; }
    /// <summary>True when the setter is <c>init</c>-only, so the generated accessor must be <c>init</c>.</summary>
    public bool IsInitOnly { get; }

    public bool Equals(MockProperty? other)
    {
      if (other is null) return false;
      return Name == other.Name && TypeFqn == other.TypeFqn && CallBase == other.CallBase && HasSetter == other.HasSetter && AccessModifier == other.AccessModifier && IsInitOnly == other.IsInitOnly;
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
        h = h * 31 + (AccessModifier?.GetHashCode() ?? 0);
        h = h * 31 + IsInitOnly.GetHashCode();
        return h;
      }
    }
  }

  internal sealed class MockIndexer : IEquatable<MockIndexer>
  {
    public MockIndexer(string returnTypeFqn, (string Type, string Name)[] parameters, bool hasGetter, bool hasSetter, bool callBase, string accessModifier = "public ")
    {
      ReturnTypeFqn = returnTypeFqn;
      Parameters = parameters;
      HasGetter = hasGetter;
      HasSetter = hasSetter;
      CallBase = callBase;
      AccessModifier = accessModifier;
    }

    public string ReturnTypeFqn { get; }
    public (string Type, string Name)[] Parameters { get; }
    public bool HasGetter { get; }
    public bool HasSetter { get; }
    public bool CallBase { get; }
    public string AccessModifier { get; }

    public bool Equals(MockIndexer? other)
    {
      if (other is null) return false;
      if (ReturnTypeFqn != other.ReturnTypeFqn) return false;
      if (HasGetter != other.HasGetter) return false;
      if (HasSetter != other.HasSetter) return false;
      if (CallBase != other.CallBase) return false;
      if (AccessModifier != other.AccessModifier) return false;
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
        h = h * 31 + (AccessModifier?.GetHashCode() ?? 0);
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
    public MockEvent(string name, string handlerTypeFqn, string accessModifier = "public ")
    {
      Name = name;
      HandlerTypeFqn = handlerTypeFqn;
      AccessModifier = accessModifier;
    }

    public string Name { get; }
    public string HandlerTypeFqn { get; }
    public string AccessModifier { get; }

    public bool Equals(MockEvent? other) => other is not null && Name == other.Name && HandlerTypeFqn == other.HandlerTypeFqn && AccessModifier == other.AccessModifier;

    public override bool Equals(object? obj) => obj is MockEvent e && Equals(e);

    public override int GetHashCode()
    {
      unchecked
      {
        var h = (Name?.GetHashCode() ?? 0) * 397 ^ (HandlerTypeFqn?.GetHashCode() ?? 0);
        h = h * 31 + (AccessModifier?.GetHashCode() ?? 0);
        return h;
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
