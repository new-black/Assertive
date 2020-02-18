using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Assertive
{
  internal static class ExpressionHelper
  {
    public static object? EvaluateExpression(Expression expression)
    {
      var lambda = Expression.Lambda(expression).Compile(true);

      var value = lambda.DynamicInvoke();

      if (value != null && expression.NodeType == ExpressionType.Convert
                        && expression is UnaryExpression unaryExpression
                        && unaryExpression.Operand.Type.GetUnderlyingType().IsEnum
                        && !unaryExpression.Type.IsEnum
                        && Enum.IsDefined(unaryExpression.Operand.Type.GetUnderlyingType(), value))
      {
        return unaryExpression.Operand.Type.GetUnderlyingType().Name + "." +
               Enum.ToObject(unaryExpression.Operand.Type.GetUnderlyingType(), value);
      }

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

        var parameters = useLambdaOverload
          ? new[] { instanceExpression, node.Arguments[1] }
          : new[] { instanceExpression };

        return Expression.Lambda<Func<int>>(Expression.Call(null,
          GetCountMethod(useLambdaOverload)
            .MakeGenericMethod(TypeHelper.GetTypeInsideEnumerable(instanceExpression.Type)),
          parameters)).Compile(true)();
      }

      return null;
    }

    public static object GetConstantExpressionValue(Expression expression)
    {
      if (expression is ConstantExpression c)
      {
        return c.Value;
      }

      return ((ConstantExpression)((UnaryExpression)expression).Operand).Value;
    }

    public static bool IsConstantExpression(Expression expression)
    {
      return expression is ConstantExpression
             || (expression.NodeType == ExpressionType.Convert
                 && expression is UnaryExpression unaryExpression
                 && unaryExpression.Operand is ConstantExpression);
    }

    public static string ExpressionToString(Expression expression)
    {
      var rewriter = new ExpressionRewriter();

      return ExpressionStringBuilder.ExpressionToString(rewriter.Visit(expression));
    }
  }
}