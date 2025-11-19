using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Helpers;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class EqualityPattern : IFriendlyMessagePattern
  {
    public static bool IsEqualityComparison(Expression expression)
    {
      return IsCallToEqualsMethod(expression)
             || IsEqualityOperator(expression);
    }

    private static bool IsEqualityOperator(Expression expression)
    {
      return expression.NodeType is ExpressionType.Equal or ExpressionType.NotEqual;
    }

    public static bool IsCallToEqualsMethod(Expression expression)
    {
      return EqualsMethodShouldBeTrue(expression) || EqualsMethodShouldBeFalse(expression);
    }

    public static bool EqualsMethodShouldBeTrue(Expression expression)
    {
      return expression is MethodCallExpression { Method.Name: "Equals" };
    }

    public static bool EqualsMethodShouldBeFalse(Expression expression)
    {
      return expression.NodeType == ExpressionType.Not
             && expression is UnaryExpression { Operand: MethodCallExpression { Method.Name: "Equals" } };
    }

    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsEqualityComparison(failedAssertion.Expression);
    }

    public static Expression? GetLeftSide(Expression assertion)
    {
      if (IsCallToEqualsMethod(assertion))
      {
        if (EqualsMethodShouldBeTrue(assertion))
        {
          return ((MethodCallExpression)assertion).Object;
        }

        return ((MethodCallExpression)((UnaryExpression)assertion).Operand).Object;
      }

      return ((BinaryExpression)assertion).Left;
    }

    public static Expression GetRightSide(Expression assertion, Expression left)
    {
      var right = GetRightSideImpl(assertion);

      if (IsConstantExpression(right) && left.NodeType == ExpressionType.Convert
                                                       && left is UnaryExpression unaryExpression
                                                       && unaryExpression.Operand.Type.GetUnderlyingType().IsEnum)
      {
        var enumType = unaryExpression.Operand.Type.GetUnderlyingType();
        var rightValue = GetConstantExpressionValue(right);

        if (rightValue != null && Enum.IsDefined(enumType, rightValue))
        {
          return Expression.Constant(Enum.ToObject(enumType, rightValue));
        }
      }

      return right;
    }

    private static Expression GetRightSideImpl(Expression assertion)
    {
      if (IsCallToEqualsMethod(assertion))
      {
        if (EqualsMethodShouldBeTrue(assertion))
        {
          return ((MethodCallExpression)assertion).Arguments[0];
        }

        return ((MethodCallExpression)((UnaryExpression)assertion).Operand).Arguments[0];
      }

      return ((BinaryExpression)assertion).Right;
    }

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return null;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    [
      new NullPattern(),
      new LengthPattern(),
      new EqualsPattern(),
      new NotEqualsPattern()
    ];
  }
}