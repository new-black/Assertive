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

    public FormattableString TryGetFriendlyMessage(Assertion assertion)
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

      return $"Expected {instance} to contain {expectedContainedValueExpression} but it did not.";
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}