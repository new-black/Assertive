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
      return expression is MethodCallExpression { Method.Name: nameof(string.StartsWith) or nameof(string.EndsWith), Arguments.Count: >= 1 } methodCallExpression 
             && methodCallExpression.Arguments[0].Type == typeof(string);
    }

    public ExpectedAndActual TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var startsWith = IsStartsOrEndsWithCall(assertion.Expression);

      var methodCallExpression = (MethodCallExpression)assertion.ExpressionWithoutNegation;

      var method = methodCallExpression.Method.Name == nameof(string.StartsWith) 
        ? "start with" : "end with";
      
      var arg = methodCallExpression.Arguments[0];

      var instance = GetInstanceOfMethodCall(methodCallExpression);
      if (IsConstantExpression(arg))
      {
        return new ExpectedAndActual()
        {
          Expected = $"""
                      {instance}: should{(startsWith ? " " : " not ")}{method} {arg}.
                      """,
          Actual = $"{instance}: {instance?.ToValue()}"

        };
      }

      return new ExpectedAndActual()
      {
        Expected = $"""
                    {instance}: should{(startsWith ? " " : " not ")}{method} {arg}.

                    {arg}: {arg.ToValue()}
                    """,
        Actual = $"{instance}: {instance?.ToValue()}"
      };
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = [];
  }
}