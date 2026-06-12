using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class BoolMemberClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      switch (expr)
      {
        case IdentifierNameSyntax:
          break;
        case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression }:
          break;
        default:
          return null;
      }

      // A bare bool member/local (BoolPattern). The operand is compiled only so its
      // captured locals are available for the LOCALS section; the value is not needed
      // (the delegate already evaluated it).
      ctx.Call.Kind = InterceptionKind.Bool;
      return CallSiteAnalyzer.SetLeft(ctx.Call, ctx.Compiler, expr, ctx.Bindings, ctx.Display);
    }
  }
}
