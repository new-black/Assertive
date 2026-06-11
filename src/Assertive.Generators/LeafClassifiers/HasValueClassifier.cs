using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class HasValueClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      if (expr is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "HasValue", RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } hasValueAccess)
        return null;
      if (!CallSiteAnalyzer.IsNullableValueType(ctx.Model.GetTypeInfo(hasValueAccess.Expression, ctx.Ct).Type))
        return null;

      ctx.Call.Kind = InterceptionKind.HasValue;
      return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, hasValueAccess.Expression, ctx.Bindings, ctx.Display);
    }
  }
}
