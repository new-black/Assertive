using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Assertive.Config;
using Assertive.Helpers;

namespace Assertive.Expressions
{
  internal static class ExpressionHelper
  {
    public static ExpressionValue ToValue(this Expression expression)
    {
      return new ExpressionValue(expression);
    }
    
    public static UnquotedExpression ToUnquoted(this Expression expression)
    {
      return new UnquotedExpression(expression);
    }
    
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
        return new ExpressionEnumValue(unaryExpression.Operand.Type.GetUnderlyingType(), value);
      }

      return value;
    }

    public static Expression? GetInstanceOfMethodCall(MethodCallExpression methodCallExpression)
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
        select m).First();
    }

    public static int? GetCollectionItemCount(Expression instanceExpression)
    {
      var instance = EvaluateExpression(instanceExpression);

      if (instance is IEnumerable && TypeHelper.GetTypeInsideEnumerable(instanceExpression.Type) is {} typeInsideEnumerable)
      {
        var parameters = new[] { instanceExpression };

        return Expression.Lambda<Func<int>>(Expression.Call(null,
          GetCountMethod(false)
            .MakeGenericMethod(typeInsideEnumerable),
          parameters)).Compile(true)();
      }

      return null;
    }

    public static int? GetCollectionItemCount(Expression instanceExpression, MethodCallExpression node)
    {
      var instance = EvaluateExpression(instanceExpression);

      if (instance is IEnumerable && TypeHelper.GetTypeInsideEnumerable(instanceExpression.Type) is {} typeInsideEnumerable)
      {
        var useLambdaOverload = node.Arguments.Count == 2 && node.Arguments[1] is LambdaExpression;

        var parameters = useLambdaOverload
          ? new[] { instanceExpression, node.Arguments[1] }
          : new[] { instanceExpression };

        return Expression.Lambda<Func<int>>(Expression.Call(null,
          GetCountMethod(useLambdaOverload)
            .MakeGenericMethod(typeInsideEnumerable),
          parameters)).Compile(true)();
      }

      return null;
    }

    public static object? GetConstantExpressionValue(Expression expression)
    {
      if (expression is ConstantExpression c)
      {
        return c.Value;
      }

      if (expression is NamedConstantExpression n)
      {
        return n.Value;
      }

      var operand = ((UnaryExpression)expression).Operand;

      return GetConstantExpressionValue(operand);
    }

    public static bool IsConstantExpression(Expression expression)
    {
      return expression is ConstantExpression
             || expression is NamedConstantExpression
             || (expression.NodeType == ExpressionType.Convert
                 && expression is UnaryExpression unaryExpression
                 && (IsConstantExpression(unaryExpression.Operand)));
    }

    private static string GetQuotationPattern(Expression expression, bool allowQuotation)
    {
      if (IsConstantExpression(expression) || !allowQuotation)
      {
        return ExpressionQuotationPatterns.None;
      }
      
      return Configuration.ExpressionQuotationPattern ?? ExpressionQuotationPatterns.None;
    }

    public static string ExpressionToString(Expression expression, bool allowQuotation = true)
    {
      var rewriter = new ExpressionRewriter();

      var rewritten = rewriter.Visit(expression);
      
      var expressionAsString = rewritten != null ? ExpressionStringBuilder.ExpressionToString(rewritten) : "";

      return string.Format(GetQuotationPattern(expression, allowQuotation), expressionAsString);
    }

    public static Expression ReplaceParameter(Expression expression, ParameterExpression parameter,
      Expression replacement)
    {
      return new ParameterVisitor(parameter, replacement).Visit(expression);
    }

    private class ParameterVisitor : ExpressionVisitor
    {
      private readonly ParameterExpression _parameter;
      private readonly Expression _replacement;

      public ParameterVisitor(ParameterExpression parameter, Expression replacement)
      {
        _parameter = parameter;
        _replacement = replacement;
      }

      private Expression ReplaceParameter(ParameterExpression parameter)
      {
        if (parameter == _parameter)
        {
          return _replacement;
        }

        return parameter;
      }

      protected override Expression VisitParameter(ParameterExpression node)
      {
        return ReplaceParameter(node);
      }
    }
  }

  internal enum CustomExpressionTypes
  {
    NamedConstant = -1
  }
}