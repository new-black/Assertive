using System;
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

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var typeAssertion = ((TypeBinaryExpression)assertion.ExpressionWithoutNegation);

      var result = ExpressionHelper.EvaluateExpression(typeAssertion.Expression);

      var expectedType = TypeHelper.TypeNameToString(typeAssertion.TypeOperand);
      
      if (result == null)
      {
        return new ExpectedAndActual()
        {
          Expected = $"{typeAssertion.Expression} should {(assertion.IsNegated ? "not " : "")}be of type {expectedType}.",
          Actual = $"It was null."
        };
      }
      
      return !assertion.IsNegated ? new ExpectedAndActual()
      {
        Expected = $"{typeAssertion.Expression} should be of type {expectedType}.",
        Actual = $"Type: {TypeHelper.TypeNameToString(result.GetType())}."
      } : new ExpectedAndActual()
      {
        Expected = $"{typeAssertion.Expression} should not be of type {expectedType}.",
        Actual = $"Type: {TypeHelper.TypeNameToString(result.GetType())}."
      };
    }

    public IFriendlyMessagePattern[] SubPatterns => [];
  }
}