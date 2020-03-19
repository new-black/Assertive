using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class BoolPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.ExpressionPossiblyNegated is MemberExpression;
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      if (assertion.IsNegated)
      {
        return $"Expected {((UnaryExpression)assertion.Expression).Operand} to be false.";
      }

      return $"Expected {assertion.Expression} to be true.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = 
    {
      new HasValuePattern(),
    };
  }
}