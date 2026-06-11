using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Assertive.Generators
{
  /// <summary>
  /// Classifies a single assertion expression form. Implement one class per expression family.
  /// Return null if the expression is not handled by this classifier (fall through to next);
  /// true if classification succeeded; false if the form matched but degraded.
  /// </summary>
  internal interface ILeafClassifier
  {
    bool? TryClassify(ClassificationContext ctx, ExpressionSyntax expr, bool outerNegated, bool leafContext);
  }
}
