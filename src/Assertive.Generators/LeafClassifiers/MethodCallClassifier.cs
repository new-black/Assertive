using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class MethodCallClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      if (expr is not InvocationExpressionSyntax methodCall
          || methodCall.Expression is not MemberAccessExpressionSyntax methodAccess
          || !methodAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
          || !methodCall.ArgumentList.Arguments.All(a => a.NameColon == null)
          || ctx.Model.GetSymbolInfo(methodCall, ctx.Ct).Symbol is not IMethodSymbol calledMethod)
        return null;

      switch (calledMethod.Name)
      {
        case "Contains" when calledMethod.ReturnType.SpecialType == SpecialType.System_Boolean
                             && methodCall.ArgumentList.Arguments.Count >= 1
                             && (!calledMethod.IsStatic || calledMethod.ReducedFrom != null):
        {
          var arg = methodCall.ArgumentList.Arguments[0].Expression;

          ctx.Call.Kind = InterceptionKind.Contains;
          ctx.Call.StringInstance = ctx.Model.GetTypeInfo(methodAccess.Expression, ctx.Ct).Type?.SpecialType == SpecialType.System_String;
          return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, methodAccess.Expression, ctx.Bindings, ctx.Display)
                 && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, arg, CallSiteAnalyzer.StripParens(arg), ctx.Bindings, ctx.Display);
        }

        case "StartsWith" or "EndsWith" when !calledMethod.IsStatic
                                             && calledMethod.Parameters.Length >= 1
                                             && calledMethod.Parameters[0].Type.SpecialType == SpecialType.System_String
                                             && methodCall.ArgumentList.Arguments.Count >= 1:
        {
          var arg = methodCall.ArgumentList.Arguments[0].Expression;

          ctx.Call.Kind = InterceptionKind.StartsEndsWith;
          ctx.Call.ComparisonLabel = calledMethod.Name == "StartsWith" ? "start with" : "end with";
          return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, methodAccess.Expression, ctx.Bindings, ctx.Display)
                 && CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, arg, CallSiteAnalyzer.StripParens(arg), ctx.Bindings, ctx.Display);
        }

        case "Any" when CallSiteAnalyzer.IsLinqEnumerableMethod(calledMethod):
        {
          var arguments = methodCall.ArgumentList.Arguments;

          ctx.Call.Kind = InterceptionKind.Any;
          ctx.Call.OperandDisplay = ctx.Display?.Invoke(methodAccess.Expression) ?? methodAccess.Expression.ToString();
          // The whole call as display: collection locals stay in the LOCALS section
          // (the old LocalsProvider showed them for Any).
          ctx.Call.LeftDisplay = ctx.Display?.Invoke(methodCall) ?? methodCall.ToString();

          if (arguments.Count != 0
              && !(arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax }))
          {
            return false;
          }

          // Collection first so LOCALS lists captures in order of appearance.
          var collection = ctx.Compiler.Compile(methodAccess.Expression, ctx.Bindings);

          if (collection == null)
          {
            return false;
          }

          if (arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax filterBody } filterLambda)
          {
            ctx.Call.FilterSource = filterBody.ToString();

            // Compiling the filter registers its captured locals for the LOCALS
            // section; only the negated form actually evaluates it (for the count).
            var typedFilter = ctx.Compiler.CompileTypedOnly(filterLambda);

            if (outerNegated)
            {
              // Needs the count of filter-matching items: typed collection + filter.
              var typedCollection = ctx.Compiler.CompileTypedOnly(methodAccess.Expression);

              if (typedCollection == null || typedFilter == null)
              {
                return false;
              }

              ctx.Call.LeftSource = $"global::System.Linq.Enumerable.Count({typedCollection}, {typedFilter})";
              return true;
            }
          }

          ctx.Call.LeftSource = $"__A.EnumerableCount({collection})";
          return true;
        }

        case "All" when CallSiteAnalyzer.IsLinqEnumerableMethod(calledMethod)
                        && ctx.Bindings == null
                        && methodCall.ArgumentList.Arguments.Count == 1
                        && methodCall.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax { Body: ExpressionSyntax allFilterBody } allFilterLambda:
        {
          var filterParameter = allFilterLambda switch
          {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1 } singleParameter } => singleParameter[0],
            _ => null,
          };

          if (filterParameter == null
              || ctx.Model.GetDeclaredSymbol(filterParameter, ctx.Ct) is not { } parameterSymbol)
          {
            return false;
          }

          ctx.Call.Kind = InterceptionKind.All;
          ctx.Call.Negated = outerNegated;
          ctx.Call.OperandDisplay = new AnonymousMemberExpander().Visit(methodAccess.Expression)?.ToString()
                                    ?? methodAccess.Expression.ToString();
          // The whole call as display: collection locals stay in the LOCALS section.
          ctx.Call.LeftDisplay = methodCall.ToString();
          ctx.Call.FilterSource = allFilterBody.ToString();
          ctx.Call.CollectionIsMethodCall = CallSiteAnalyzer.StripParens(methodAccess.Expression) is InvocationExpressionSyntax;
          ctx.Call.LeftSource = ctx.Compiler.Compile(methodAccess.Expression)!;

          if (ctx.Call.LeftSource == null)
          {
            return false;
          }

          if (outerNegated)
          {
            // NotAllPattern: expected-only message, no per-item analysis.
            return true;
          }

          var itemBindings = new Dictionary<string, CallSiteAnalyzer.LambdaBinding>
          {
            [parameterSymbol.Name] = new CallSiteAnalyzer.LambdaBinding(
              CallSiteAnalyzer.IsUsableType(parameterSymbol.Type, ctx.Model.Compilation)
                ? $"(({parameterSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})__i)"
                : null,
              "__i"),
          };

          // Finding the failing items requires evaluating the filter per item: typed
          // when the element type is nameable, otherwise an Equals-based fallback for
          // (in)equality bodies (anonymous element types).
          if (ctx.Compiler.CompileTypedOnly(allFilterBody, itemBindings) is { } typedFilter)
          {
            ctx.Call.AllFilterFunc = $"(__i, __j) => (bool)({typedFilter})";
          }

          // Classify the filter body as a per-item sub-assertion, displayed with the
          // parameter renamed to "item" (the old AllPattern bound the item as a
          // NamedConstantExpression named "item" and re-ran the full analyzer).
          var renamer = new ParamRenameRewriter(ctx.Model, parameterSymbol, ctx.Ct);
          Func<ExpressionSyntax, string> itemDisplay = e => renamer.Visit(e)?.ToString() ?? e.ToString();

          var subNegated = false;
          var subCore = CallSiteAnalyzer.StripParens(allFilterBody);

          while (subCore is PrefixUnaryExpressionSyntax subNegation && subNegation.IsKind(SyntaxKind.LogicalNotExpression))
          {
            subNegated = !subNegated;
            subCore = CallSiteAnalyzer.StripParens(subNegation.Operand);
          }

          var subCall = new InterceptedCall { Negated = subNegated };

          if (CallSiteAnalyzer.ClassifyForm(ctx.WithCall(subCall).WithBindings(itemBindings).WithDisplay(itemDisplay), subCore, subNegated))
          {
            ctx.Call.AllSubCall = subCall;

            if (ctx.Call.AllFilterFunc == null && subCall.Kind == InterceptionKind.Equality)
            {
              var equalsCheck = $"__A.ObjectEquals({subCall.LeftSource}, {subCall.RightSource})";
              ctx.Call.AllFilterFunc = $"(__i, __j) => {(subCall.Negated ? "!" : "")}{equalsCheck}";
            }
          }

          return ctx.Call.AllFilterFunc != null;
        }

        // On newer TFMs (first-class spans + OverloadResolutionPriority), array
        // receivers bind to MemoryExtensions.SequenceEqual instead of
        // Enumerable.SequenceEqual; the semantics (element-wise equality, optional
        // comparer) are identical and the runtime diff enumerates the captured
        // values, so both bindings classify the same.
        case "SequenceEqual" when (CallSiteAnalyzer.IsLinqEnumerableMethod(calledMethod) || CallSiteAnalyzer.IsMemoryExtensionsMethod(calledMethod))
                                  && !outerNegated
                                  && methodCall.ArgumentList.Arguments.Count is 1 or 2:
        {
          var arg = methodCall.ArgumentList.Arguments[0].Expression;

          // A genuinely span-typed receiver/argument (not an array converted at the
          // call) is a ref struct: it cannot be captured or enumerated as a value.
          if (ctx.Model.GetTypeInfo(methodAccess.Expression, ctx.Ct).Type is { IsRefLikeType: true }
              || ctx.Model.GetTypeInfo(arg, ctx.Ct).Type is { IsRefLikeType: true })
          {
            return false;
          }

          ctx.Call.Kind = InterceptionKind.SequenceEqual;

          if (!CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, methodAccess.Expression, ctx.Bindings, ctx.Display)
              || !CallSiteAnalyzer.SetRight(ctx.Call, ctx.Compiler, arg, CallSiteAnalyzer.StripParens(arg), ctx.Bindings, ctx.Display))
          {
            return false;
          }

          if (methodCall.ArgumentList.Arguments.Count == 2)
          {
            ctx.Call.ComparerSource = ctx.Compiler.Compile(methodCall.ArgumentList.Arguments[1].Expression);

            if (ctx.Call.ComparerSource == null)
            {
              return false;
            }
          }

          ctx.Call.TypeAccessor = calledMethod.TypeArguments.Length == 1
            ? ctx.Compiler.TypeAccessor(calledMethod.TypeArguments[0])
            : null;

          return true;
        }

        default:
          return false;
      }
    }

    /// <summary>
    /// Expands implicit anonymous-object member declarators for display
    /// (`new { n, i }` to `new { n = n, i = i }`), matching the old expression-tree
    /// rendering of anonymous types.
    /// </summary>
    private sealed class AnonymousMemberExpander : CSharpSyntaxRewriter
    {
      public override SyntaxNode? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
      {
        if (node.NameEquals != null)
        {
          return base.VisitAnonymousObjectMemberDeclarator(node);
        }

        var name = node.Expression switch
        {
          IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
          MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
          _ => null,
        };

        if (name == null)
        {
          return base.VisitAnonymousObjectMemberDeclarator(node);
        }

        var nameEquals = SyntaxFactory.NameEquals(
          SyntaxFactory.IdentifierName(name),
          SyntaxFactory.Token(SyntaxKind.EqualsToken)
            .WithLeadingTrivia(SyntaxFactory.Space)
            .WithTrailingTrivia(SyntaxFactory.Space));

        return node
          .WithNameEquals(nameEquals.WithLeadingTrivia(node.Expression.GetLeadingTrivia()))
          .WithExpression(node.Expression.WithoutLeadingTrivia());
      }
    }

    /// <summary>
    /// Renames an All() filter's lambda parameter to "item" for display purposes
    /// (the bound-item naming of the old AllPattern's per-item sub-analysis).
    /// </summary>
    private sealed class ParamRenameRewriter : CSharpSyntaxRewriter
    {
      private readonly SemanticModel _model;
      private readonly IParameterSymbol _parameter;
      private readonly CancellationToken _ct;

      public ParamRenameRewriter(SemanticModel model, IParameterSymbol parameter, CancellationToken ct)
      {
        _model = model;
        _parameter = parameter;
        _ct = ct;
      }

      public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
      {
        if (node.Identifier.ValueText == _parameter.Name
            && SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(node, _ct).Symbol, _parameter))
        {
          return SyntaxFactory.IdentifierName("item").WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
      }
    }
  }
}
