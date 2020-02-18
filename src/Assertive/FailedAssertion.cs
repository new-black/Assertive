using System;
using System.Linq.Expressions;

namespace Assertive
{
  internal class FailedAssertion
  {
    public FailedAssertion(Expression assertion, Exception? ex)
    {
      Expression = assertion;
      Exception = ex;
    }

    public Expression Expression { get; }
    public Exception? Exception { get; }
  }
}

