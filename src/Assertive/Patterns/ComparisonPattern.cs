using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Assertive.Patterns
{
  internal class ComparisonPattern : IFriendlyMessagePattern
  {
    private readonly HashSet<ExpressionType> _comparisonTypes = new HashSet<ExpressionType>()
    {
      ExpressionType.GreaterThan,
      ExpressionType.GreaterThanOrEqual,
      ExpressionType.LessThan,
      ExpressionType.LessThanOrEqual,
      ExpressionType.Equal,
      ExpressionType.NotEqual
    };
    
    public bool IsMatch(Expression expression)
    {
      return _comparisonTypes.Contains(expression.NodeType);
    }

    public FormattableString TryGetFriendlyMessage(Assertion assertion)
    {
      return default;
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } =
    {
      new LengthPattern(),
      new EqualsPattern(),
      new LessThanOrGreaterThanPattern()
    };
  }
}