using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using Assertive.Expressions;

namespace Assertive.ExceptionPatterns
{
  internal abstract class LambdaAwareExpressionVisitor : ExpressionVisitor
  {
    // Lambda iteration context
    public int? LambdaItemIndex { get; private set; }
    public object? LambdaItem { get; private set; }
    public Expression? CollectionExpression { get; private set; }

    private readonly Dictionary<ParameterExpression, object?> _parameterBindings = new();

    /// <summary>
    /// Returns true if the visitor has found what it's looking for (e.g., cause of exception).
    /// Used to stop iteration through collection items.
    /// </summary>
    protected abstract bool HasFoundResult { get; }

    /// <summary>
    /// Tries to visit a method call that has a lambda argument by iterating through
    /// the collection and binding the lambda parameter to each item.
    /// </summary>
    /// <returns>True if this was a lambda method call that was handled, false otherwise.</returns>
    protected bool TryVisitLambdaMethodCall(MethodCallExpression node)
    {
      // Find the lambda argument
      LambdaExpression? lambda = null;
      foreach (var arg in node.Arguments)
      {
        if (arg is LambdaExpression l)
        {
          lambda = l;
          break;
        }
      }

      if (lambda == null || lambda.Parameters.Count == 0)
      {
        return false;
      }

      // Get the collection - either the object (instance method) or the first argument (static/extension method)
      var collection = node.Object ?? node.Arguments[0];

      // Try to evaluate the collection
      object? collectionValue;
      try
      {
        collectionValue = EvaluateExpressionWithBindings(collection);
      }
      catch
      {
        return false;
      }

      if (collectionValue is not IEnumerable enumerable)
      {
        return false;
      }

      var parameter = lambda.Parameters[0];
      var index = 0;

      // Iterate through collection to find the item that causes the issue
      foreach (var item in enumerable)
      {
        _parameterBindings[parameter] = item;

        Visit(lambda.Body);

        if (HasFoundResult)
        {
          LambdaItemIndex = index;
          LambdaItem = item;
          CollectionExpression = collection;
          // Keep the binding so ReplaceParametersWithBindings can be called later
          return true;
        }

        _parameterBindings.Remove(parameter);
        index++;
      }

      // Didn't find anything inside the lambda - let normal processing continue
      return false;
    }

    /// <summary>
    /// Evaluates an expression, replacing any bound lambda parameters with their values.
    /// </summary>
    protected object? EvaluateExpressionWithBindings(Expression node)
    {
      var nodeToEvaluate = ReplaceParametersWithBindings(node);

      var lambda = Expression.Lambda(nodeToEvaluate);
      return lambda.Compile(ExpressionHelper.ShouldUseInterpreter(lambda)).DynamicInvoke();
    }

    /// <summary>
    /// Replaces any bound lambda parameters in the expression with their constant values.
    /// </summary>
    public Expression ReplaceParametersWithBindings(Expression node)
    {
      foreach (var (param, value) in _parameterBindings)
      {
        node = ExpressionHelper.ReplaceParameter(node, param, Expression.Constant(value, param.Type));
      }

      return node;
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
      // Don't visit inside lambdas by default - they are handled specially in TryVisitLambdaMethodCall
      // Visiting them here would fail due to unbound parameters
      return node;
    }
  }
}
