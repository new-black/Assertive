using System;
using System.Linq;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class IsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return failedAssertion.ExpressionWithoutNegation is TypeBinaryExpression { NodeType: ExpressionType.TypeIs } t 
             && t.TypeOperand != typeof(object);
    }

    public FormattableString? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var typeAssertion = ((TypeBinaryExpression)assertion.ExpressionWithoutNegation);

      var result = ExpressionHelper.EvaluateExpression(typeAssertion.Expression);

      var expectedType = TypeHelper.TypeNameToString(typeAssertion.TypeOperand);
      
      if (result == null)
      {
        return $"Expected {typeAssertion.Expression} to be of type {expectedType} but it was null.";
      }

      return !assertion.IsNegated ? 
        $"Expected {typeAssertion.Expression} to be of type {expectedType} but its actual type was {TypeHelper.TypeNameToString(result.GetType())}." 
        : (FormattableString)$"Expected {typeAssertion.Expression} to not be of type {expectedType}.";
    }

    public IFriendlyMessagePattern[] SubPatterns => [];
  }
}