using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class NullPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return (failedAssertion.Expression.NodeType == ExpressionType.Equal || failedAssertion.Expression.NodeType == ExpressionType.NotEqual)
             && failedAssertion.Expression is BinaryExpression b
             && ((b.Right is ConstantExpression c
             && c.Value == null) || (b.Right is DefaultExpression && b.Right.Type.IsClass));
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      if (assertion.Expression is BinaryExpression b)
      {
        if (b.NodeType == ExpressionType.Equal)
        {
          return $"Expected {b.Left} to be null but it was {ExpressionHelper.EvaluateExpression(b.Left)} instead.";
        }
        else
        {
          return $"Expected {b.Left} to not be null.";
        }
      }

      return default;
    }

    public IFriendlyMessagePattern[] SubPatterns => Array.Empty<IFriendlyMessagePattern>();
  }
}