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

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCallExpression = (MethodCallExpression)((UnaryExpression)assertion.Expression).Operand;

      var collectionExpression = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);

      var filter = (LambdaExpression)methodCallExpression.Arguments[1];

      return new ExpectedAndActual()
      {
        Expected = $"Not all items of {collectionExpression} should match the filter {filter.Body}.",
        Actual = null
      };
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}