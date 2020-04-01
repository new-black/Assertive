using System;
using System.Linq.Expressions;

namespace Assertive.Analyzers
{
  internal class FailedAssertion
  {
    public FailedAssertion(Expression assertion, Exception? ex)
    {
      Expression = assertion;
      Exception = ex;

      if (Expression.NodeType == ExpressionType.Not && Expression is UnaryExpression unaryExpression)
      {
        NegatedExpression = unaryExpression.Operand;
      }
    }

    public Expression Expression { get; }
    public Exception? Exception { get; }
    public Expression? NegatedExpression { get; }
    public bool IsNegated => NegatedExpression != null;

    public Expression ExpressionPossiblyNegated => NegatedExpression ?? Expression;
  }
}

