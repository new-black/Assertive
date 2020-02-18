using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class StartsWithAndEndsWithPattern : IFriendlyMessagePattern
  {
    public bool IsMatch(Expression expression)
    {
      return IsStartsWithCall(expression)
        || (expression is UnaryExpression unaryExpression 
        && expression.NodeType == ExpressionType.Not
        && IsStartsWithCall(unaryExpression.Operand));
    }

    private static bool IsStartsWithCall(Expression expression)
    {
      return expression is MethodCallExpression methodCallExpression
             && (methodCallExpression.Method.Name == nameof(string.StartsWith) || methodCallExpression.Method.Name == nameof(string.EndsWith))
             && methodCallExpression.Arguments.Count >= 1
             && methodCallExpression.Arguments[0].Type == typeof(string);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var startsWith = IsStartsWithCall(assertion.Expression);
      
      var methodCallExpression = (MethodCallExpression)(startsWith
        ? assertion.Expression
        : ((UnaryExpression)assertion.Expression).Operand);

      var method = methodCallExpression.Method.Name == nameof(string.StartsWith) 
        ? "start with" : "end with";
      
      var arg = methodCallExpression.Arguments[0];

      var instance = GetInstanceOfMethodCall(methodCallExpression);

      if (ExpressionHelper.IsConstantExpression(arg))
      {
        return $@"Expected {instance} to{(startsWith ? " " : " not ")}{method} {arg}.

Value of {instance}: {EvaluateExpression(instance)}";  
      }
      
      return $@"Expected {instance} to{(startsWith ? " " : " not ")}{method} {arg} (value: {EvaluateExpression(arg)}).

Value of {instance}: {EvaluateExpression(instance)}";   
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}