using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class NotAllPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return expression is UnaryExpression unaryExpression
             && unaryExpression.NodeType == ExpressionType.Not
             && AllPattern.IsAllMethodCall(unaryExpression.Operand);
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