using System;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Patterns
{
  internal class ContainsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsContainsMethodCall(failedAssertion.ExpressionWithoutNegation);
    }
    
    private static bool IsContainsMethodCall(Expression expression)
    {
      return expression is MethodCallExpression { Method.Name: nameof(Enumerable.Contains) };
    }
    
    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var notContains = assertion.IsNegated;

      var callExpression = (MethodCallExpression)assertion.ExpressionWithoutNegation;

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
        expectedValueString = $"{expectedContainedValueExpression} (value: {expectedContainedValueExpression.ToValue()})";
      }
      
      if (instance != null && instance.Type == typeof(string))
      {
        return $"Expected {instance} (value: {instance.ToValue()}) to{(notContains ? " not " : " ")}contain {expectedValueString}.";
      }

      return $"Expected {instance} to{(notContains ? " not " : " ")}contain {expectedValueString}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}