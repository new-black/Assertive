using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class EqualsPattern : IFriendlyMessagePattern
  {
    public static bool IsEqualComparison(Expression expression)
    {
      return expression.NodeType == ExpressionType.Equal
             || expression.NodeType == ExpressionType.NotEqual;
    }
    
    public bool IsMatch(Expression expression)
    {
      return IsEqualComparison(expression);
    }

    public FormattableString TryGetFriendlyMessage(Assertion assertion)
    {
      var binaryExpression = (BinaryExpression)assertion.Expression;

      var comparison = binaryExpression.NodeType switch
      {
        ExpressionType.Equal => " ",
        ExpressionType.NotEqual => " not "
      };

      if (binaryExpression.Right.NodeType == ExpressionType.Constant)
      {
        return $"Expected {binaryExpression.Left} to{comparison}equal {binaryExpression.Right} but {binaryExpression.Left} was {ExpressionHelper.EvaluateExpression(binaryExpression.Left)}.";
      }

      return $"Expected {binaryExpression.Left} to{comparison}equal {binaryExpression.Right} but {binaryExpression.Left} was {ExpressionHelper.EvaluateExpression(binaryExpression.Left)} while {binaryExpression.Right} was {ExpressionHelper.EvaluateExpression(binaryExpression.Right)}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = 
    {
      new NullPattern(),
    };
  }
}