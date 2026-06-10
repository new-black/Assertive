using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Assertive.Poc.Generator
{
  /// <summary>
  /// Proof of concept: intercepts PocAssert.That(() => ...) calls and replaces them with
  /// generated code that evaluates each sub-expression exactly once, captures the values,
  /// and produces a decomposed failure message — without System.Linq.Expressions.
  /// </summary>
  [Generator]
  public sealed class AssertInterceptorGenerator : IIncrementalGenerator
  {
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      var calls = context.SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => node is InvocationExpressionSyntax
          {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "That" }
          },
          transform: static (ctx, ct) => Analyze(ctx, ct))
        .Where(static c => c is not null);

      context.RegisterSourceOutput(calls.Collect(), static (spc, all) => Emit(spc, all!));
    }

    private sealed class InterceptedCall
    {
      public int LocationVersion;
      public string LocationData = "";
      public string DisplayLocation = "";
      public string OriginalExpression = "";
      public List<(string Name, string Type)> CapturedLocals { get; } = new();
      public List<Leaf> Leaves { get; } = new();
      public string? FallbackReason;
    }

    /// <summary>One conjunct of the asserted expression (split on &amp;&amp;), the
    /// equivalent of a leaf in Assertive's AssertionTreeProvider output.</summary>
    private sealed class Leaf
    {
      public string Source = "";

      // Comparison leaf (x.Length == 5)
      public string? Left;
      public string? Right;
      public string? Operator;
      public bool RightIsConstant;

      // Any leaf (xs.Any(...), !xs.Any(...)) — the POC port of Assertive's AnyPattern
      public string? AnyReceiver;
      public string? AnyFilter;
      public string? AnyFilterBody;
      public bool Negated;
    }

    private static InterceptedCall? Analyze(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
      var invocation = (InvocationExpressionSyntax)ctx.Node;

      if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
          || method.Name != "That"
          || method.ContainingType?.Name != "PocAssert")
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
      };

      if (invocation.ArgumentList.Arguments.Count == 0
          || invocation.ArgumentList.Arguments[0].Expression is not ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } body })
      {
        call.FallbackReason = "the assertion is not an expression-bodied lambda";
        return call;
      }

      call.OriginalExpression = body.ToString();

      // Every identifier that the expression reads from its enclosing scope must be
      // reconstructed inside the interceptor by reading the lambda's closure object.
      foreach (var id in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
      {
        // Skip member names (the `Length` in `x.Length`); their receiver is what matters.
        if (id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id)
        {
          continue;
        }

        var symbol = ctx.SemanticModel.GetSymbolInfo(id, ct).Symbol;

        switch (symbol)
        {
          // A parameter of a lambda nested inside the assertion (the `p` in `xs.Any(p => ...)`)
          // is declared within the code we paste, so it needs no reconstruction.
          case IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } }:
            break;
          case ILocalSymbol local when !local.Type.IsAnonymousType:
            AddCapturedLocal(call, local.Name, local.Type);
            break;
          case IParameterSymbol param when !param.Type.IsAnonymousType:
            AddCapturedLocal(call, param.Name, param.Type);
            break;
          case INamespaceOrTypeSymbol:
            break; // a qualifier like `Math` in `Math.Abs(...)` — resolves statically
          default:
            call.FallbackReason = $"identifier '{id.Identifier.ValueText}' is not a local or parameter (POC limitation)";
            return call;
        }
      }

      CollectLeaves(body, call.Leaves, ctx.SemanticModel, ct);

      return call;
    }

    private static void AddCapturedLocal(InterceptedCall call, string name, ITypeSymbol type)
    {
      if (call.CapturedLocals.Any(l => l.Name == name))
      {
        return;
      }

      call.CapturedLocals.Add((name, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private static void CollectLeaves(ExpressionSyntax expression, List<Leaf> leaves, SemanticModel semanticModel, CancellationToken ct)
    {
      expression = StripParens(expression);

      if (expression is BinaryExpressionSyntax andExpr && andExpr.IsKind(SyntaxKind.LogicalAndExpression))
      {
        CollectLeaves(andExpr.Left, leaves, semanticModel, ct);
        CollectLeaves(andExpr.Right, leaves, semanticModel, ct);
        return;
      }

      var leaf = new Leaf { Source = expression.ToString() };

      var (negated, inner) = expression is PrefixUnaryExpressionSyntax notExpr && notExpr.IsKind(SyntaxKind.LogicalNotExpression)
        ? (true, StripParens(notExpr.Operand))
        : (false, expression);

      if (TryAnalyzeAny(inner, negated, leaf, semanticModel, ct))
      {
        leaves.Add(leaf);
        return;
      }

      if (!negated
          && expression is BinaryExpressionSyntax comparison
          && comparison.Kind() is SyntaxKind.EqualsExpression
            or SyntaxKind.NotEqualsExpression
            or SyntaxKind.LessThanExpression
            or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression
            or SyntaxKind.GreaterThanOrEqualExpression)
      {
        var left = StripParens(comparison.Left);
        var right = StripParens(comparison.Right);

        // `var __right = null` would not compile; treat as a plain boolean leaf.
        if (!left.IsKind(SyntaxKind.NullLiteralExpression) && !right.IsKind(SyntaxKind.NullLiteralExpression))
        {
          leaf.Left = left.ToString();
          leaf.Right = right.ToString();
          leaf.Operator = comparison.OperatorToken.Text;
          leaf.RightIsConstant = right is LiteralExpressionSyntax;
        }
      }

      leaves.Add(leaf);
    }

    /// <summary>
    /// Recognizes `xs.Any()` and `xs.Any(x => ...)` leaves. Unlike the runtime AnyPattern,
    /// which matches any method *named* Any, the semantic model lets us verify the call
    /// actually binds to System.Linq.Enumerable.Any.
    /// </summary>
    private static bool TryAnalyzeAny(ExpressionSyntax expression, bool negated, Leaf leaf, SemanticModel semanticModel, CancellationToken ct)
    {
      if (expression is not InvocationExpressionSyntax
          {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Any" } memberAccess
          } invocation)
      {
        return false;
      }

      if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
          || method.ContainingType?.ToDisplayString() != "System.Linq.Enumerable")
      {
        return false;
      }

      var arguments = invocation.ArgumentList.Arguments;

      if (arguments.Count == 1)
      {
        // Method groups and block-bodied lambdas are out of scope for the POC.
        if (arguments[0].Expression is not SimpleLambdaExpressionSyntax { ExpressionBody: { } filterBody } filter)
        {
          return false;
        }

        leaf.AnyFilter = filter.ToString();
        leaf.AnyFilterBody = filterBody.ToString();
      }
      else if (arguments.Count != 0)
      {
        return false;
      }

      leaf.AnyReceiver = memberAccess.Expression.ToString();
      leaf.Negated = negated;
      return true;
    }

    private static ExpressionSyntax StripParens(ExpressionSyntax expression)
    {
      while (expression is ParenthesizedExpressionSyntax p)
      {
        expression = p.Expression;
      }

      return expression;
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<InterceptedCall?> calls)
    {
      if (calls.IsDefaultOrEmpty)
      {
        return;
      }

      var sb = new StringBuilder();

      sb.AppendLine("// <auto-generated/>");
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
      sb.AppendLine("namespace System.Runtime.CompilerServices");
      sb.AppendLine("{");
      sb.AppendLine("  [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
      sb.AppendLine("  file sealed class InterceptsLocationAttribute : global::System.Attribute");
      sb.AppendLine("  {");
      sb.AppendLine("    public InterceptsLocationAttribute(int version, string data)");
      sb.AppendLine("    {");
      sb.AppendLine("      _ = version;");
      sb.AppendLine("      _ = data;");
      sb.AppendLine("    }");
      sb.AppendLine("  }");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("namespace Assertive.Poc.Generated");
      sb.AppendLine("{");
      sb.AppendLine("  internal static class PocAssertInterceptors");
      sb.AppendLine("  {");

      var index = 0;

      foreach (var call in calls)
      {
        if (call == null)
        {
          continue;
        }

        EmitCall(sb, call, index++);
        sb.AppendLine();
      }

      EmitHelpers(sb);

      sb.AppendLine("  }");
      sb.AppendLine("}");

      spc.AddSource("PocAssertInterceptors.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitCall(StringBuilder sb, InterceptedCall call, int index)
    {
      sb.AppendLine($"    // {call.DisplayLocation}");
      sb.AppendLine($"    [global::System.Runtime.CompilerServices.InterceptsLocation({call.LocationVersion}, {Quote(call.LocationData)})]");
      sb.AppendLine($"    public static void That{index}(global::System.Func<bool> condition, string expr = \"\")");
      sb.AppendLine("    {");

      if (call.FallbackReason != null)
      {
        sb.AppendLine("      if (!condition())");
        sb.AppendLine("      {");
        sb.AppendLine($"        throw new global::Assertive.Poc.PocAssertionException($\"Assertion failed: {{expr}}\\n\\n(intercepted, but not decomposed: {call.FallbackReason})\");");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        return;
      }

      if (call.CapturedLocals.Count > 0)
      {
        sb.AppendLine("      // Reconstruct the captured variables by reading the lambda's closure object.");
        sb.AppendLine("      // The compiler stores captured locals as public fields named after the variable.");
        sb.AppendLine("      var __target = condition.Target ?? throw new global::System.InvalidOperationException(\"POC: expected a closure\");");
        sb.AppendLine("      var __targetType = __target.GetType();");

        foreach (var (name, type) in call.CapturedLocals)
        {
          sb.AppendLine($"      {type} {name} = ({type})__targetType.GetField({Quote(name)})!.GetValue(__target)!;");
        }
      }

      var localsArray = string.Join(", ", call.CapturedLocals.Select(l => $"({Quote(l.Name)}, (object?){l.Name})"));
      sb.AppendLine($"      var __locals = new (string Name, object? Value)[] {{ {localsArray} }};");

      foreach (var leaf in call.Leaves)
      {
        sb.AppendLine();
        sb.AppendLine($"      // condition: {leaf.Source.Replace("\r", " ").Replace("\n", " ")}");
        sb.AppendLine("      {");

        if (leaf.AnyReceiver != null)
        {
          EmitAnyLeaf(sb, leaf);
        }
        else if (leaf.Left != null && leaf.Right != null && leaf.Operator != null)
        {
          sb.AppendLine($"        var __left = {leaf.Left};");
          sb.AppendLine($"        var __right = {leaf.Right};");
          sb.AppendLine($"        if (!(__left {leaf.Operator} __right))");
          sb.AppendLine("        {");
          // A literal operand restates itself ("3 = 3"); only show evaluated operands.
          var operands = leaf.RightIsConstant
            ? $"({Quote(leaf.Left)}, (object?)__left)"
            : $"({Quote(leaf.Left)}, (object?)__left), ({Quote(leaf.Right)}, (object?)__right)";
          sb.AppendLine($"          throw Fail(expr, {Quote(leaf.Source)}, new (string Expr, object? Value)[] {{ {operands} }}, __locals);");
          sb.AppendLine("        }");
        }
        else
        {
          sb.AppendLine($"        if (!({leaf.Source}))");
          sb.AppendLine("        {");
          sb.AppendLine($"          throw Fail(expr, {Quote(leaf.Source)}, new (string Expr, object? Value)[] {{ }}, __locals);");
          sb.AppendLine("        }");
        }

        sb.AppendLine("      }");
      }

      sb.AppendLine("    }");
    }

    private static void EmitAnyLeaf(StringBuilder sb, Leaf leaf)
    {
      // Where the runtime AnyPattern constructs an Enumerable.Count call as an expression
      // tree at failure time, here we simply emit the call. Counting enumerates the source
      // a second time, matching the runtime pattern's existing behavior.
      var anyCall = leaf.AnyFilter != null
        ? $"global::System.Linq.Enumerable.Any(__collection, {leaf.AnyFilter})"
        : "global::System.Linq.Enumerable.Any(__collection)";

      // A negated assertion (`!xs.Any(...)`) fails when Any returns true, and the message
      // reports how many items matched the filter.
      var countCall = leaf.Negated && leaf.AnyFilter != null
        ? $"global::System.Linq.Enumerable.Count(__collection, {leaf.AnyFilter})"
        : "global::System.Linq.Enumerable.Count(__collection)";

      sb.AppendLine($"        var __collection = {leaf.AnyReceiver};");
      sb.AppendLine($"        if ({(leaf.Negated ? "" : "!")}{anyCall})");
      sb.AppendLine("        {");
      sb.AppendLine($"          var __count = {countCall};");
      sb.AppendLine($"          throw FailAny(expr, {Quote(leaf.Source)}, {Quote(leaf.AnyReceiver!)}, {(leaf.AnyFilterBody != null ? Quote(leaf.AnyFilterBody) : "null")}, __count, {(leaf.Negated ? "true" : "false")}, __locals);");
      sb.AppendLine("        }");
    }

    private static void EmitHelpers(StringBuilder sb)
    {
      sb.AppendLine("""
        private static global::Assertive.Poc.PocAssertionException Fail(
          string expr,
          string failedCondition,
          (string Expr, object? Value)[] operands,
          (string Name, object? Value)[] locals)
        {
          var sb = StartMessage(expr, failedCondition);

          if (operands.Length > 0)
          {
            sb.AppendLine();
            foreach (var operand in operands)
            {
              sb.Append("  ").Append(operand.Expr).Append(" = ").Append(Format(operand.Value)).AppendLine();
            }
          }

          AppendLocals(sb, locals);

          return new global::Assertive.Poc.PocAssertionException(sb.ToString());
        }

        // The POC port of Assertive's AnyPattern message (Patterns/AnyPattern.cs).
        private static global::Assertive.Poc.PocAssertionException FailAny(
          string expr,
          string failedCondition,
          string collection,
          string? filterBody,
          int count,
          bool negated,
          (string Name, object? Value)[] locals)
        {
          var filterString = filterBody != null ? $" that match the filter {filterBody}" : "";

          string expected;
          string actual;

          if (negated)
          {
            expected = $"Collection {collection} should not contain any items{filterString}.";
            actual = $"It contained {count} {(count == 1 ? "item" : "items")}.";
          }
          else
          {
            expected = $"Collection {collection} should contain some items{filterString}.";
            actual = filterBody == null || count == 0
              ? "It contained no items."
              : "It contained no items matching the filter.";
          }

          var sb = StartMessage(expr, failedCondition);
          sb.AppendLine();
          sb.Append("Expected: ").Append(expected).AppendLine();
          sb.Append("Actual: ").Append(actual).AppendLine();

          AppendLocals(sb, locals);

          return new global::Assertive.Poc.PocAssertionException(sb.ToString());
        }

        private static global::System.Text.StringBuilder StartMessage(string expr, string failedCondition)
        {
          var sb = new global::System.Text.StringBuilder();
          sb.Append("Assertion failed: ").Append(expr).AppendLine();

          if (failedCondition != expr)
          {
            sb.AppendLine();
            sb.Append("Failed condition: ").Append(failedCondition).AppendLine();
          }

          return sb;
        }

        private static void AppendLocals(global::System.Text.StringBuilder sb, (string Name, object? Value)[] locals)
        {
          if (locals.Length == 0)
          {
            return;
          }

          sb.AppendLine();
          sb.AppendLine("Locals:");

          foreach (var local in locals)
          {
            sb.Append("  ").Append(local.Name).Append(" = ").Append(Format(local.Value)).AppendLine();
          }
        }

        private static string Format(object? value)
        {
          switch (value)
          {
            case null:
              return "null";
            case string s:
              return "\"" + s + "\"";
            case bool b:
              return b ? "true" : "false";
            case global::System.Collections.IEnumerable items:
            {
              var sb = new global::System.Text.StringBuilder("[");
              var i = 0;

              foreach (var item in items)
              {
                if (i == 10)
                {
                  sb.Append(", ...");
                  break;
                }

                if (i > 0)
                {
                  sb.Append(", ");
                }

                sb.Append(Format(item));
                i++;
              }

              sb.Append(']');
              return sb.ToString();
            }
            default:
              return value.ToString() ?? "null";
          }
        }
""");
    }

    private static string Quote(string text) => SymbolDisplay.FormatLiteral(text, quote: true);
  }
}
