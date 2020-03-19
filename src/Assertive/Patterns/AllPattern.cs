using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class AllPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsAllMethodCall(failedAssertion.Expression);
    }

    public static bool IsAllMethodCall(Expression expression)
    {
      return expression is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == nameof(Enumerable.All)
             && methodCallExpression.Arguments.Count >= 2
             && ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression).Type.IsType<IEnumerable>()
             && methodCallExpression.Arguments[1] is LambdaExpression lambdaExpression
             && lambdaExpression.ReturnType == typeof(bool);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCallExpression = (MethodCallExpression)assertion.Expression;

      var collectionExpression = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);

      var filter = (LambdaExpression)methodCallExpression.Arguments[1];

      var collection = ((IEnumerable)ExpressionHelper.EvaluateExpression(collectionExpression)!).Cast<object>();

      var compiledFilter = filter.Compile(true);

      var invalidMatches = new List<object>();

      var moreItems = false;
      var invalidCount = 0;

      foreach (var obj in collection)
      {
        var isMatch = (bool)compiledFilter.DynamicInvoke(obj);

        if (!isMatch)
        {
          invalidCount++;
          
          if (invalidMatches.Count == 10)
          {
            moreItems = true;
          }
          else
          {
            invalidMatches.Add(obj);
          }
        }
      }

      var i = 0;

      var items = invalidMatches.Select(obj => $"[{i++}]: {StringQuoter.Quote(obj)}").ToList();

      if (invalidCount == 1)
      {
        return
          $@"Expected all items of {collectionExpression} to match the filter {filter.Body}, but this item did not: {invalidMatches[0]}";
      }

      var invalidMatchesString = $"but these {invalidCount} items did not{(moreItems ? " (first 10)" : "")}:";

      return $@"Expected all items of {collectionExpression} to match the filter {filter.Body}, {invalidMatchesString}

{string.Join("," + Environment.NewLine, items)}";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}