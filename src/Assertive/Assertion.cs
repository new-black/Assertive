using System;
using System.Linq.Expressions;

namespace Assertive
{
  internal class Assertion
  {
    public Assertion(Expression<Func<bool>> expression, object? message, Expression<Func<object>>? context)
    {
      Expression = expression;
      Message = message;
      Context = context;
    }
    
    public Expression<Func<bool>> Expression { get; }
    public object? Message { get; }
    public Expression<Func<object>>? Context { get; }
  }
}