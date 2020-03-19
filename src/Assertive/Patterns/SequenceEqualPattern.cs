using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using static Assertive.EnumerableHelper;
using static Assertive.StringQuoter;

namespace Assertive.Patterns
{
  internal class SequenceEqualPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.Expression is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == nameof(Enumerable.SequenceEqual)
             && methodCallExpression.Arguments.Count >= 2
             && ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression).Type.IsType<IEnumerable>()
             && methodCallExpression.Arguments[1].Type.IsType<IEnumerable>();
    }

    private struct Difference
    {
      public int Index;
      public object? ValueSequence1;
      public bool HasValueSequence1;

      public object? ValueSequence2;
      public bool HasValueSequence2;
    }

    private object? GetDefaultComparer(Type enumerableType)
    {
      var defaultComparerType = typeof(EqualityComparer<>).MakeGenericType(enumerableType);

      return defaultComparerType.GetProperty(nameof(EqualityComparer<int>.Default))?.GetValue(null);
    }

    private Func<object?, object?, bool>? GetComparerFunc(MethodCallExpression methodCallExpression, Type enumerableType)
    {
      Func<object?, object?, bool>? equals = null;

      object? comparer;
      
      if (methodCallExpression.Arguments.Count >= 3)
      {
        comparer = ExpressionHelper.EvaluateExpression(methodCallExpression.Arguments[2]);
      }
      else
      {
        comparer = GetDefaultComparer(enumerableType);
      }

      if (comparer == null)
      {
        return null;
      }

      var comparerType = comparer.GetType();

      if (comparer is IEqualityComparer c)
      {
        equals = (x, y) => c.Equals(x, y);
      }
      else
      {
        if (enumerableType != null)
        {
          var comparerInterface = (from i in comparerType.GetInterfaces()
            where i.IsGenericType
                  && i.GetGenericTypeDefinition() == typeof(IEqualityComparer<>)
                  && i.GenericTypeArguments.Length == 1
                  && enumerableType.IsType(i.GenericTypeArguments[0])
            select i).FirstOrDefault();

          if (comparerInterface != null)
          {
            var method = comparerInterface.GetMethod(nameof(IEqualityComparer<int>.Equals), new[] { enumerableType, enumerableType });

            if (method != null)
            {
              equals = (x, y) => (bool)method.Invoke(comparer, new[] { x, y });
            }
          }
        }
      }

      return equals;
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var methodCallExpression = (MethodCallExpression)assertion.Expression;
      var collection1Expression = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);
      var collection2Expression = methodCallExpression.Arguments[1];

      var collection1 = ((IEnumerable)ExpressionHelper.EvaluateExpression(collection1Expression)!).Cast<object>();
      var collection2 = ((IEnumerable)ExpressionHelper.EvaluateExpression(collection2Expression)!).Cast<object>();
      
      var differences = new List<Difference>();

      var enumerableType = TypeHelper.GetTypeInsideEnumerable(collection1Expression.Type);

      if (enumerableType == null)
      {
        return null;
      }

      var equals = GetComparerFunc(methodCallExpression, enumerableType);

      FormattableString result;

      if (equals != null)
      {
        var index = 0;

        var hasMoreDifferences = false;

        var differenceCount = 0;

        IEnumerator<object>? enumerator1 = null;
        IEnumerator<object>? enumerator2 = null;

        try
        {
          enumerator1 = collection1.GetEnumerator();
          enumerator2 = collection2.GetEnumerator();

          while (true)
          {
            var moveNext1 = enumerator1.MoveNext();
            var moveNext2 = enumerator2.MoveNext();

            if (!moveNext1 && !moveNext2)
            {
              break;
            }

            var current1 = moveNext1 ? enumerator1.Current : null;
            var current2 = moveNext2 ? enumerator2.Current : null;

            if (!equals(current1, current2))
            {
              differenceCount++;

              if (differences.Count == 10)
              {
                hasMoreDifferences = true;
              }
              else
              {
                differences.Add(new Difference()
                {
                  Index = index,
                  ValueSequence1 = current1,
                  HasValueSequence1 = moveNext1,
                  ValueSequence2 = current2,
                  HasValueSequence2 = moveNext2
                });
              }
            }

            index++;
          }
        }
        finally
        {
          enumerator1?.Dispose();
          enumerator2?.Dispose();
        }

        var differencesString = differences.Select(d =>
            $"[{d.Index}]: {(d.HasValueSequence1 ? Quote(d.ValueSequence1) ?? "null" : "(no value)")} <> {(d.HasValueSequence2 ? Quote(d.ValueSequence2) ?? "null" : "(no value)")}")
          .ToList();

        result =
          $@"Expected {collection1Expression} to be equal to {collection2Expression}, but there {(differenceCount > 1 ? $"were {differenceCount} differences" : "was 1 difference")}{(hasMoreDifferences ? " (first 10)" : "")}:

{string.Join("," + Environment.NewLine, differencesString)}";
      }
      else
      {
        result = $@"Expected {collection1Expression} to be equal to {collection2Expression} but they were not.";
      }

      return $@"{result}

Value of {collection1Expression}: {EnumerableToString(collection1)}
Value of {collection2Expression}: {EnumerableToString(collection2)}";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}