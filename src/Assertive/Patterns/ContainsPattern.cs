using System;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Assertive.Patterns
{
  internal class ContainsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsContainsMethodCall(failedAssertion.ExpressionPossiblyNegated);
    }
    
    private static bool IsContainsMethodCall(Expression expression)
    {
      return expression is MethodCallExpression methodCallExpression
             && methodCallExpression.Method.Name == nameof(Enumerable.Contains);
    }
    
    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var notContains = assertion.IsNegated;

      var callExpression = (MethodCallExpression)assertion.ExpressionPossiblyNegated;

      var instance = callExpression.Object;

      var isExtensionMethod = callExpression.Method.IsDefined(typeof(ExtensionAttribute), false);

      if (isExtensionMethod)
      {
        instance = callExpression.Arguments.First();
      }

      var expectedContainedValueExpression = callExpression.Arguments.Skip(isExtensionMethod ? 1 : 0).First();

      FormattableString expectedValueString;

      if (ExpressionHelper.IsConstantExpression(expectedContainedValueExpression))
      {
        expectedValueString = $"{expectedContainedValueExpression}";
      }
      else
      {
        expectedValueString = $"{expectedContainedValueExpression} (value: {ExpressionHelper.EvaluateExpression(expectedContainedValueExpression)})";
      }
      
      if (instance != null && instance.Type == typeof(string))
      {
        return $"Expected {instance} (value: {ExpressionHelper.EvaluateExpression(instance)}) to{(notContains ? " not " : " ")}contain {expectedValueString}.";
      }

      return $"Expected {instance} to{(notContains ? " not " : " ")}contain {expectedValueString}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}