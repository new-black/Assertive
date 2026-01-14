using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class KeyNotFoundPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is KeyNotFoundException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var visitor = new KeyNotFoundVisitor();

      visitor.Visit(assertion.Expression);

      if (visitor.CauseOfKeyNotFound == null)
      {
        return null;
      }

      var keyExpression = visitor.CauseOfKeyNotFound.Arguments[0];
      var dictionaryExpression = visitor.CauseOfKeyNotFound.Object;

      // Evaluate the key that was not found
      object? keyValue = null;
      try
      {
        keyValue = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(keyExpression));
      }
      catch
      {
        // Could not evaluate key
      }

      // Get available keys from the dictionary
      string? availableKeys = null;
      if (dictionaryExpression != null)
      {
        try
        {
          var dict = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(dictionaryExpression));
          if (dict is IDictionary dictionary)
          {
            var keys = dictionary.Keys.Cast<object>().Take(10).ToList();
            var hasMore = dictionary.Count > 10;
            availableKeys = keys.Count == 0
              ? "(empty)"
              : string.Join(", ", keys.Select(k => Serializer.Serialize(k))) + (hasMore ? ", ..." : "");
          }
        }
        catch
        {
          // Could not get keys
        }
      }

      var keyString = keyValue != null
        ? ExpressionHelper.IsConstantExpression(keyExpression)
          ? $"{Serializer.Serialize(keyValue)}"
          : $"{ExpressionHelper.ExpressionToString(keyExpression, allowQuotation: false)} (value: {Serializer.Serialize(keyValue)})"
        : $"{ExpressionHelper.ExpressionToString(keyExpression, allowQuotation: false)}";

      FormattableString message = availableKeys != null
        ? (FormattableString)$"KeyNotFoundException caused by accessing key {keyString} on {dictionaryExpression}. Available keys: {availableKeys}."
        : $"KeyNotFoundException caused by accessing key {keyString} on {dictionaryExpression}.";

      // Append lambda item context if available
      if (visitor.LambdaItemIndex.HasValue)
      {
        var serializedItem = Serializer.Serialize(visitor.LambdaItem);
        message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{visitor.LambdaItemIndex}] of {visitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
      }

      return new HandledException(message, visitor.CauseOfKeyNotFound);
    }

    private class KeyNotFoundVisitor : LambdaAwareExpressionVisitor
    {
      public MethodCallExpression? CauseOfKeyNotFound { get; private set; }

      protected override bool HasFoundResult => CauseOfKeyNotFound != null;

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        var result = base.VisitMethodCall(node);

        if (CauseOfKeyNotFound != null)
        {
          return result;
        }

        // Check for dictionary indexer access (get_Item)
        if (node.Method.Name == "get_Item" && node.Object != null && IsDictionaryType(node.Object.Type) && ThrowsKeyNotFoundException(node))
        {
          CauseOfKeyNotFound = node;
        }

        return result;
      }

      private static bool IsDictionaryType(Type type)
      {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
          return true;
        }

        return type.GetInterfaces().Any(i =>
          i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
      }

      private bool ThrowsKeyNotFoundException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is KeyNotFoundException)
        {
          return true;
        }
        catch (InvalidOperationException)
        {
          // Unbound parameters - can't evaluate
          return false;
        }
      }
    }
  }
}