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

    /// <summary>
    /// If the complete assertion is negated (`!name.Contains("bob")`) then this is the expression without that negation (so: `name.Contains("bob")`).
    /// </summary>
    public Expression ExpressionWithoutNegation => NegatedExpression ?? Expression;
  }
}

