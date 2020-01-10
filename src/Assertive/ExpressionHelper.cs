using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Assertive
{
  internal static class ExpressionHelper
  {
    public static object EvaluateExpression(Expression expression)
    {
      var lambda = Expression.Lambda(expression).Compile(true);

      var value = lambda.DynamicInvoke();

      return StringQuoter.Quote(value);
    }
    
    public static Expression GetInstanceOfMethodCall(MethodCallExpression methodCallExpression)
    {
      var instance = methodCallExpression.Object;

      var isExtensionMethod = methodCallExpression.Method.IsDefined(typeof(ExtensionAttribute), false);

      if (isExtensionMethod)
      {
        instance = methodCallExpression.Arguments.First();
      }

      return instance;
    }
    
    
    private static MethodInfo GetCountMethod(bool useLambdaOverload)
    {
      return (from m in typeof(Enumerable).GetMethods()
        where m.Name == nameof(Enumerable.Count)
              && m.IsGenericMethod
              && m.GetParameters().Length == (useLambdaOverload ? 2 : 1)
        select m).FirstOrDefault();
    }

    public static int? GetCollectionItemCount(Expression instanceExpression)
    {
      var instance = EvaluateExpression(instanceExpression);

      if (instance is IEnumerable)
      {
        var parameters = new[] { instanceExpression };
          
        return Expression.Lambda<Func<int>>(Expression.Call(null,
          GetCountMethod(false)
            .MakeGenericMethod(TypeHelper.GetTypeInsideEnumerable(instanceExpression.Type)),
          parameters)).Compile(true)();
      }

      return null;
    }
    
    public static int? GetCollectionItemCount(Expression instanceExpression, MethodCallExpression node)
    {
      var instance = EvaluateExpression(instanceExpression);

      if (instance is IEnumerable)
      {
        var useLambdaOverload = node.Arguments.Count == 2 && node.Arguments[1] is LambdaExpression;

        var parameters = useLambdaOverload ? new[] { instanceExpression, node.Arguments[1] } : new[] { instanceExpression };
          
        return Expression.Lambda<Func<int>>(Expression.Call(null,
          GetCountMethod(useLambdaOverload)
            .MakeGenericMethod(TypeHelper.GetTypeInsideEnumerable(instanceExpression.Type)),
          parameters)).Compile(true)();
      }

      return null;
    }

    public static Expression<Func<bool>> ConvertTruthyAssertion(Expression<Func<object>> assertion)
    {
      var notNull = Expression.NotEqual(assertion.Body, Expression.Constant(null));

      var isNotBoolean = Expression.Not(Expression.TypeIs(assertion.Body, typeof(bool)));
      
      
      
      return Expression.Lambda<Func<bool>>(notNull);
    }
  }
}