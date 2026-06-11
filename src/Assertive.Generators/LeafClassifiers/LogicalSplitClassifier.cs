using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  internal sealed class LogicalSplitClassifier : ILeafClassifier
  {
    public bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext)
    {
      if (expr is not BinaryExpressionSyntax logical
          || !SplitTreeBuilder.IsSplittableLogical(logical, ctx.Syntax, ctx.Ct))
        return null;

      // `a && b` is the documented way to combine multiple asserts in one statement:
      // each conjunct is split into its own leaf assertion with operator semantics
      // (AssertionTreeProvider/AssertionTreeExecutor parity). Negated composites and
      // nested sub-classifications stay opaque.
      if (outerNegated || ctx.Bindings != null)
        return false;

      ctx.Call.Kind = InterceptionKind.Split;

      // For pure &&-only chains, pattern variables declared in one conjunct (obj is T u)
      // can be used in subsequent conjuncts (u.Name == "Bob"). The bindings accumulate
      // left-to-right and are only safe with &&-short-circuit semantics. Mixed chains
      // (||, &, |) don't get this accumulator; cross-conjunct pattern vars there degrade
      // to Opaque (correct — C# scoping is more complex).
      var patternBindings = SplitTreeBuilder.IsPureAndAlsoSyntax(logical)
        ? new Dictionary<string, CallSiteAnalyzer.LambdaBinding>()
        : null;
      ctx.Call.SplitRoot = SplitTreeBuilder.BuildSplitPart(ctx.Syntax, logical, ctx.Call, ctx.Compiler, ctx.Ct, patternBindings);
      return ctx.Call.SplitRoot != null;
    }
  }
}
