using System.Linq.Expressions;

namespace Assertive.Expressions
{
  public class UnquotedExpression
  {
    public UnquotedExpression(Expression expression)
    {
      Expression = expression;
    }
    
    public Expression Expression { get; }
  }
}