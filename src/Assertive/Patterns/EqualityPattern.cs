using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

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
      return expression.NodeType == ExpressionType.Equal
             || expression.NodeType == ExpressionType.NotEqual;
    }

    public static bool IsCallToEqualsMethod(Expression expression)
    {
      return EqualsMethodShouldBeTrue(expression) || EqualsMethodShouldBeFalse(expression);
    }

    public static bool EqualsMethodShouldBeTrue(Expression expression)
    {
      return expression is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == "Equals";
    }

    public static bool EqualsMethodShouldBeFalse(Expression expression)
    {
      return expression.NodeType == ExpressionType.Not
             && expression is UnaryExpression unaryExpression
             && unaryExpression.Operand is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == "Equals";
    }
    
    public bool IsMatch(Expression expression)
    {
      return IsEqualityComparison(expression);
    }

    public static Expression GetLeftSide(Expression assertion)
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
    
    public static Expression GetRightSide(Expression assertion)
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
    
    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      return null;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = 
    {
      new NullPattern(),
      new LengthPattern(),
      new EqualsPattern(),
      new NotEqualsPattern()
    };
  }
}