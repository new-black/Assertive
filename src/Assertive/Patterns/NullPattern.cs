using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class NullPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return (expression.NodeType == ExpressionType.Equal || expression.NodeType == ExpressionType.NotEqual)
             && expression is BinaryExpression b
             && b.Right is ConstantExpression c
             && c.Value == null;
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var b = assertion.Expression as BinaryExpression;
      
      if (b.NodeType == ExpressionType.Equal)
      {
        return $"Expected {b.Left} to be null but it was {ExpressionHelper.EvaluateExpression(b.Left)} instead.";
      }
      else
      {
        return $"Expected {b.Left} to not be null.";
      }
    }

    public IFriendlyMessagePattern[] SubPatterns => Array.Empty<IFriendlyMessagePattern>();
  }
}