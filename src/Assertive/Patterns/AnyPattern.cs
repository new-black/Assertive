using System;
using System.Linq;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class AnyPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsAnyMethodCall(failedAssertion.ExpressionPossiblyNegated);
    }

    private static bool IsAnyMethodCall(Expression expression)
    {
      return expression is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == nameof(Enumerable.Any);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var notAny = assertion.IsNegated;

      MethodCallExpression methodCallExpression;

      if (notAny)
      {
        methodCallExpression = (MethodCallExpression)((UnaryExpression)assertion.Expression).Operand;
      }
      else
      {
        methodCallExpression = (MethodCallExpression)assertion.Expression;
      }

      var collection = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);
      
      FormattableString filterString;

      if (methodCallExpression.Arguments.Count == 2 && methodCallExpression.Arguments[1] is LambdaExpression lambdaExpression)
      {
        filterString = $" that match the filter {lambdaExpression.Body}";
      }
      else
      {
        filterString = $"";
      }

      string actualCountString = "";
      
      if (notAny)
      {
        var actualCount = ExpressionHelper.GetCollectionItemCount(collection, methodCallExpression);
        actualCountString = $" but it actually contained {actualCount} {(actualCount == 1 ? "item" : "items")}";
      }
      
      
      return $"Expected {collection} to {(notAny ? "not " : "")}contain {(notAny ? "any " : "")}items{filterString}{actualCountString}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}