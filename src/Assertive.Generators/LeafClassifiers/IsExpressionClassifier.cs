using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class IsExpressionClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      if (expr is not BinaryExpressionSyntax binary || !binary.IsKind(SyntaxKind.IsExpression))
        return null;

      if (ctx.Model.GetSymbolInfo(binary.Right, ctx.Ct).Symbol is not ITypeSymbol checkedType)
      {
        return false;
      }

      if (checkedType.SpecialType == SpecialType.System_Object)
      {
        // `x is object` is a null check (NullPattern); negated means "expected null".
        ctx.Call.Kind = InterceptionKind.Null;
        ctx.Call.Negated = outerNegated;
        return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, binary.Left, ctx.Bindings, ctx.Display);
      }

      ctx.Call.Kind = InterceptionKind.Is;
      ctx.Call.TypeAccessor = ctx.Compiler.TypeAccessor(checkedType);
      return ctx.Call.TypeAccessor != null && CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, binary.Left, ctx.Bindings, ctx.Display);
    }
  }
}
