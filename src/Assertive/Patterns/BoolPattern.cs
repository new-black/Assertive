using System;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class BoolPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return expression is MemberExpression || IsNotTrue(expression);
    }

    private static bool IsNotTrue(Expression expression)
    {
      return expression is UnaryExpression unaryExpression
             && expression.NodeType == ExpressionType.Not
             && unaryExpression.Operand is MemberExpression;
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      if (IsNotTrue(assertion.Expression))
      {
        return $"Expected {((UnaryExpression)assertion.Expression).Operand} to be false.";
      }

      return $"Expected {assertion.Expression} to be true.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}