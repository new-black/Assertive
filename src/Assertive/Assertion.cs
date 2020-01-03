using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Assertive
{
  internal class Assertion
  {
    public Assertion(Expression assertion, Exception ex)
    {
      Expression = assertion;
      Exception = ex;
    }

    public Expression Expression { get; }
    public Exception Exception { get; }
  }
}

