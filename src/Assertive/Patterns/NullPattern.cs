using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class NullPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsNullEqualityCheck(failedAssertion) 
             || IsObjectCheck(failedAssertion);
    }

    private static bool IsObjectCheck(FailedAssertion failedAssertion)
    {
      return failedAssertion.ExpressionWithoutNegation is TypeBinaryExpression { NodeType: ExpressionType.TypeIs } t 
             && t.TypeOperand == typeof(object);
    }

    private static bool IsNullEqualityCheck(FailedAssertion failedAssertion)
    {
      return failedAssertion.Expression.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
             && failedAssertion.Expression is BinaryExpression b
             && (b.Right is ConstantExpression { Value: null } || (b.Right is DefaultExpression && b.Right.Type.IsClass));
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      bool expectedNull = false;
      Expression? expression = null;
      
      if (assertion.Expression is BinaryExpression b)
      {
        if (b.NodeType == ExpressionType.Equal)
        {
          expectedNull = true;
        }

        expression = b.Left;
      }
      else if (assertion.ExpressionWithoutNegation is TypeBinaryExpression typeIsExpression )
      {
        if (assertion.IsNegated)
        {
          expectedNull = true;
        }

        expression = typeIsExpression.Expression;
      }

      if (expression != null)
      {

        if (expectedNull)
        {
          return $"Expected {expression} to be null but it was {expression.ToValue()} instead.";
        }
        else
        {
          return $"Expected {expression} to not be null.";
        }
      }

      return default;
    }

    public IFriendlyMessagePattern[] SubPatterns => [];
  }
}