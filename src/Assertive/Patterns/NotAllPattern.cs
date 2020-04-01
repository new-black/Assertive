using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class NotAllPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.IsNegated && AllPattern.IsAllMethodCall(failedAssertion.NegatedExpression!);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCallExpression = (MethodCallExpression)((UnaryExpression)assertion.Expression).Operand;

      var collectionExpression = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);

      var filter = (LambdaExpression)methodCallExpression.Arguments[1];

      return $"Did not expect all items of {collectionExpression} to match the filter {filter.Body}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}