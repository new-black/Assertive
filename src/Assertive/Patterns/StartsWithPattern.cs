using System;
using System.Linq.Expressions;
using static Assertive.ExpressionHelper;

namespace Assertive.Patterns
{
  internal class StartsWithPattern : IFriendlyMessagePattern
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
             && methodCallExpression.Method.Name == nameof(string.StartsWith)
             && methodCallExpression.Arguments.Count >= 1
             && methodCallExpression.Arguments[0].Type == typeof(string);
    }

    public FormattableString TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var startsWith = IsStartsWithCall(assertion.Expression);
      
      var methodCallExpression = (MethodCallExpression)(startsWith
        ? assertion.Expression
        : ((UnaryExpression)assertion.Expression).Operand);

      var arg = methodCallExpression.Arguments[0];

      var instance = GetInstanceOfMethodCall(methodCallExpression);

      if (arg is ConstantExpression)
      {
        return $@"Expected {instance} to{(startsWith ? " " : " not ")}start with {arg}.

Value of {instance}: {EvaluateExpression(instance)}";  
      }
      
      return $@"Expected {instance} to{(startsWith ? " " : " not ")}start with {arg} (value: {EvaluateExpression(arg)}).

Value of {instance}: {EvaluateExpression(instance)}";   
    }

    public IFriendlyMessagePattern[] SubPatterns { get; } = Array.Empty<IFriendlyMessagePattern>();
  }
}