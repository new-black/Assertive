using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

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
      return expression is MethodCallExpression { Method.Name: nameof(Enumerable.All), Arguments.Count: >= 2 } methodCallExpression 
             && ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression)?.Type.IsType<IEnumerable>() == true 
             && methodCallExpression.Arguments[1] is LambdaExpression lambdaExpression 
             && lambdaExpression.ReturnType == typeof(bool);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCallExpression = (MethodCallExpression)assertion.Expression;

      var collectionExpression = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);

      var filter = (LambdaExpression)methodCallExpression.Arguments[1];

      var collection = collectionExpression != null ? ((IEnumerable)ExpressionHelper.EvaluateExpression(collectionExpression)!).Cast<object>() : [];

      var compiledFilter = filter.Compile(ExpressionHelper.ShouldUseInterpreter(filter));

      var invalidMatches = new List<object>();

      var moreItems = false;
      var invalidCount = 0;

      var subMessages = new List<FormattableString>();

      var index = 0;
      
      foreach (var obj in collection)
      {
        if (compiledFilter.DynamicInvoke(obj) is false)
        {
          invalidCount++;

          if (invalidMatches.Count == 10)
          {
            moreItems = true;
          }
          else
          {
            invalidMatches.Add(obj);

            var namedConstant = new NamedConstantExpression("item", obj);

            var newExpression =
              Expression.Lambda<Func<bool>>(
                ExpressionHelper.ReplaceParameter(filter.Body, filter.Parameters[0], namedConstant));
            
            var analyzer = new AssertionFailureAnalyzer(new AssertionFailureContext(new Assertion(newExpression, null, null), null));

            var failures = analyzer.AnalyzeAssertionFailures();

            foreach (var failure in failures)
            {
              if (failure.Message != null)
              {
                subMessages.Add(collectionExpression is MethodCallExpression
                  ? $"[{index}] - {failure.Message}"
                  : (FormattableString)$"{collectionExpression?.ToUnquoted()}[{index}] - {failure.Message}");
              }
            }
          }
        }

        index++;
      }

      FormattableString MessagesPerItem()
      {
        return subMessages.Count switch
        {
          0 => $"",
          1 when invalidCount == 1 => $@"

{subMessages[0]}",
          _ => $@"

Messages per item:

{subMessages}"
        };
      }
      
      if (invalidCount == 1)
      {
        return
          $@"Expected all items of {collectionExpression} to match the filter {filter.Body}, but this item did not: {invalidMatches[0]}{MessagesPerItem()}";
      }

      var invalidMatchesString = $"but these {invalidCount} items did not{(moreItems ? " (first 10)" : "")}:";
      
      var items = invalidMatches.Select((obj, i) => $"[{i}]: {Serializer.Serialize(obj)}").ToList();

      return $@"Expected all items of {collectionExpression} to match the filter {filter.Body}, {invalidMatchesString}

{string.Join("," + Environment.NewLine, items)}{MessagesPerItem()}";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}