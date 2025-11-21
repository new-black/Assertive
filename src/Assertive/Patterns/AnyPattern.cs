using System;
using System.Linq;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class AnyPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsAnyMethodCall(failedAssertion.ExpressionWithoutNegation);
    }

    private static bool IsAnyMethodCall(Expression expression)
    {
      return expression is MethodCallExpression { Method.Name: nameof(Enumerable.Any) };
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
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

      bool isFiltered;

      if (methodCallExpression.Arguments is [_, LambdaExpression lambdaExpression])
      {
        filterString = $" that match the filter {lambdaExpression.Body}";
        isFiltered = true;
      }
      else
      {
        isFiltered = false;
        filterString = $"";
      }
      
      
      if (notAny)
      {
        var actualCount = collection != null ? ExpressionHelper.GetCollectionItemCount(collection, methodCallExpression) : 0;

        return new ExpectedAndActual()
        {
          Expected = $"Collection {collection} should not contain any items{filterString}.",
          Actual = $"It contained {actualCount} {(actualCount == 1 ? "item" : "items")}"
        };
      }
      else
      {
        var actualCount = collection != null ? ExpressionHelper.GetCollectionItemCount(collection) : 0;
        return new ExpectedAndActual()
        {
          Expected = $"Collection {collection} should contain some items{filterString}.",
          Actual = !isFiltered || actualCount == 0 ? $"It contained no items." : (FormattableString)$"It contained no items matching the filter."
        };
      }
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}