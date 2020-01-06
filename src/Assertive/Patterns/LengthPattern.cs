using System;
using System.Linq.Expressions;
using System.Runtime.InteropServices.ComTypes;

namespace Assertive.Patterns
{
  internal class LengthPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return (EqualsPattern.IsEqualComparison(expression) ||
              LessThanOrGreaterThanPattern.IsNumericalComparison(expression))
             && expression is BinaryExpression binaryExpression
             && IsLengthAccess(binaryExpression.Left);

    }

    private static bool IsArrayLength(Expression expression) => expression.NodeType == ExpressionType.ArrayLength;

    private static bool IsListCount(Expression expression) => expression is MemberExpression memberExpression
                                                              && memberExpression.Member.Name == "Count";

    private static bool IsCountMethod(Expression expression) => expression is MethodCallExpression methodCallExpression
                                                                && methodCallExpression.Method.Name == "Count";

    private static bool IsStringLength(Expression expression) => expression is MemberExpression memberExpression
                                                                 && memberExpression.Member.Name == "Length";
    
    private static bool IsLengthAccess(Expression expression)
    {
      return IsArrayLength(expression) || IsListCount(expression) || IsCountMethod(expression) || IsStringLength(expression);
    }

    public FormattableString TryGetFriendlyMessage(Assertion assertion)
    {
      var binaryExpression = (BinaryExpression)assertion.Expression;

      var actualLength = ExpressionHelper.EvaluateExpression(binaryExpression.Left);

      string countLabel;
      
      if (IsArrayLength(binaryExpression.Left) || IsStringLength(binaryExpression.Left))
      {
        countLabel = "length";
      }
      else
      {
        countLabel = "count";
      }

      string comparison;

      if (EqualsPattern.IsEqualComparison(assertion.Expression))
      {
        comparison = "equal to";
      }
      else if (LessThanOrGreaterThanPattern.IsNumericalComparison(assertion.Expression))
      {
        comparison = LessThanOrGreaterThanPattern.GetComparisonLabel(assertion.Expression);
      }
      else
      {
        comparison = string.Empty;
      }

      Expression operand;

      if (binaryExpression.Left is MemberExpression memberExpression)
      {
        operand = memberExpression.Expression;
      }
      else if (binaryExpression.Left is UnaryExpression unaryExpression)
      {
        operand = unaryExpression.Operand;
      }
      else if (binaryExpression.Left is MethodCallExpression methodCallExpression)
      {
        operand = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);
      }
      else
      {
        operand = binaryExpression.Left;
      }

      if (binaryExpression.Right.NodeType == ExpressionType.Constant)
      {
        return $"Expected {operand} to have a {countLabel} {comparison} {binaryExpression.Right} but the actual {countLabel} was {actualLength}.";
      }
      
      return $"Expected {operand} to have a {countLabel} {comparison} {binaryExpression.Right} ({ExpressionHelper.EvaluateExpression(binaryExpression.Right)}) but the actual {countLabel} was {actualLength}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}