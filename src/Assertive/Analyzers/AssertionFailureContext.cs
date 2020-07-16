using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Assertive.Analyzers
{
  internal class AssertionFailureContext
  {
    public Assertion Assertion { get; }
    public Exception? AssertionException { get; }
    public HashSet<Expression> EvaluatedExpressions { get; } =new HashSet<Expression>();
    
    public AssertionFailureContext(Assertion assertion, Exception? assertionException)
    {
      Assertion = assertion;
      AssertionException = assertionException;
    }

  }
}