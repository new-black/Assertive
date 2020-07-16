using System.Linq.Expressions;

namespace Assertive.Expressions
{
  internal class ExpressionValue
  {
    public Expression Expression { get; }

    public ExpressionValue(Expression expression)
    {
      Expression = expression;
    }
  }
}