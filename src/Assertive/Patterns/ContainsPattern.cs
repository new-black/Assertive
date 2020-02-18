using System;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Assertive.Patterns
{
  internal class ContainsPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      var callExpression = expression as MethodCallExpression;

      if (callExpression == null) return false;

      return callExpression.Method.Name == "Contains";
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var expression = assertion.Expression;

      var callExpression = (MethodCallExpression)expression;

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
        return $"Expected {instance} (value: {ExpressionHelper.EvaluateExpression(instance)}) to contain {expectedValueString}.";
      }

      return $"Expected {instance} to contain {expectedValueString}.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}