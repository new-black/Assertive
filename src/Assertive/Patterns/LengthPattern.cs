using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class LengthPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      var expression = failedAssertion.Expression;
      
      return (EqualityPattern.IsEqualityComparison(expression) ||
              LessThanOrGreaterThanPattern.IsNumericalComparison(expression))
             && expression is BinaryExpression binaryExpression
             && IsLengthAccess(binaryExpression.Left);

    }

    private static bool IsArrayLength(Expression expression) => expression.NodeType == ExpressionType.ArrayLength;

    private static bool IsListCount(Expression expression) => expression is MemberExpression { Member.Name: "Count" };

    private static bool IsCountMethod(Expression expression) => expression is MethodCallExpression { Method.Name: "Count" };

    private static bool IsStringLength(Expression expression) => expression is MemberExpression { Member.Name: "Length" };
    
    private static bool IsLengthAccess(Expression expression)
    {
      return IsArrayLength(expression) || IsListCount(expression) || IsCountMethod(expression) || IsStringLength(expression);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
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

      if (assertion.Expression.NodeType == ExpressionType.Equal)
      {
        comparison = "equal to";
      }
      else if (assertion.Expression.NodeType == ExpressionType.NotEqual)
      {
        comparison = "not equal to";
      }
      else if (LessThanOrGreaterThanPattern.IsNumericalComparison(assertion.Expression))
      {
        comparison = LessThanOrGreaterThanPattern.GetComparisonLabel(assertion.Expression);
      }
      else
      {
        comparison = string.Empty;
      }

      Expression? operand;

      Expression? filter = null;

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
        if (methodCallExpression.Arguments.Count >= 2 &&
            methodCallExpression.Arguments[1] is LambdaExpression lambdaExpression)
        {
          filter = lambdaExpression.Body;
        }
        
        operand = ExpressionHelper.GetInstanceOfMethodCall(methodCallExpression);
      }
      else
      {
        operand = binaryExpression.Left;
      }

      FormattableString filterString = $"";
      if (filter != null)
      {
        filterString = $" with filter {filter}";
      }

      FormattableString actualCountString;

      if (assertion.Expression.NodeType == ExpressionType.NotEqual)
      {
          actualCountString =
            $"";
      }
      else
      {
        actualCountString = $" but the actual {countLabel} was {actualLength}";
      }

      if (binaryExpression.Right.NodeType == ExpressionType.Constant)
      {
        return $"Expected {operand}{filterString} to have a {countLabel} {comparison} {binaryExpression.Right}{actualCountString}.";
      }
      
      return $"Expected {operand}{filterString} to have a {countLabel} {comparison} {binaryExpression.Right} (value: {binaryExpression.Right.ToValue()}){actualCountString}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}