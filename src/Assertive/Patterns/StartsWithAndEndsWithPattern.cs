using System;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class StartsWithAndEndsWithPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(FailedAssertion failedAssertion)
    {
      return IsStartsOrEndsWithCall(failedAssertion.ExpressionWithoutNegation);
    }

    private static bool IsStartsOrEndsWithCall(Expression expression)
    {
      return expression is MethodCallExpression methodCallExpression
             && (methodCallExpression.Method.Name == nameof(string.StartsWith) || methodCallExpression.Method.Name == nameof(string.EndsWith))
             && methodCallExpression.Arguments.Count >= 1
             && methodCallExpression.Arguments[0].Type == typeof(string);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var startsWith = IsStartsOrEndsWithCall(assertion.Expression);

      var methodCallExpression = (MethodCallExpression)assertion.ExpressionWithoutNegation;

      var method = methodCallExpression.Method.Name == nameof(string.StartsWith) 
        ? "start with" : "end with";
      
      var arg = methodCallExpression.Arguments[0];

      var instance = GetInstanceOfMethodCall(methodCallExpression);

      if (IsConstantExpression(arg))
      {
        return $@"Expected {instance} to{(startsWith ? " " : " not ")}{method} {arg}.

Value of {instance}: {instance.ToValue()}";  
      }
      
      return $@"Expected {instance} to{(startsWith ? " " : " not ")}{method} {arg} (value: {arg.ToValue()}).

Value of {instance}: {instance.ToValue()}";   
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}