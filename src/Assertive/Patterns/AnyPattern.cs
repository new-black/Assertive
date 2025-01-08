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
        var actualCount = collection != null ? ExpressionHelper.GetCollectionItemCount(collection, methodCallExpression) : 0;
        actualCountString = $" but it actually contained {actualCount} {(actualCount == 1 ? "item" : "items")}";
      }
      
      
      FormattableString result = $"Expected {collection} to {(notAny ? "not " : "")}contain {(notAny ? "any " : "")}items{filterString}{actualCountString}.";

      if (notAny)
      {
        result = $@"{result}

Value of {collection}:
";
      }

      return result;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}