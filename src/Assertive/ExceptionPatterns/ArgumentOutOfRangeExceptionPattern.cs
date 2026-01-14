using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  /// <summary>
  /// Handles ArgumentOutOfRangeException from method calls like string.Substring().
  /// Note: ArgumentOutOfRangeException from indexer access (get_Item) is handled by IndexOutOfRangeExceptionPattern.
  /// </summary>
  internal class ArgumentOutOfRangeExceptionPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is ArgumentOutOfRangeException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var visitor = new ArgumentOutOfRangeVisitor();

      visitor.Visit(assertion.Expression);

      if (visitor.CauseOfException == null)
      {
        return null;
      }

      var methodCall = visitor.CauseOfException;
      var method = methodCall.Method;
      var methodName = method.Name;

      // Get the instance (for instance methods)
      var instance = methodCall.Object;
      string? instanceValue = null;
      string instanceString = "";

      if (instance != null)
      {
        instanceString = ExpressionHelper.ExpressionToString(instance, allowQuotation: false);
        try
        {
          var value = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(instance));
          if (value is string s)
          {
            instanceValue = s;
          }
        }
        catch
        {
          // Could not evaluate
        }
      }

      // Build argument string
      var argsString = BuildArgumentsString(methodCall, visitor);

      FormattableString message;

      if (instance != null)
      {
        // Instance method like str.Substring(10)
        if (instanceValue != null && methodName == "Substring")
        {
          // Special handling for Substring - show length
          message = (FormattableString)$"ArgumentOutOfRangeException caused by calling {methodName}({argsString}) on {instanceString} (length: {instanceValue.Length}).";
        }
        else if (instanceValue != null)
        {
          message = (FormattableString)$"ArgumentOutOfRangeException caused by calling {methodName}({argsString}) on {instanceString}. Value of {instanceString}: {Serializer.Serialize(instanceValue)}";
        }
        else
        {
          message = (FormattableString)$"ArgumentOutOfRangeException caused by calling {methodName}({argsString}) on {instanceString}.";
        }
      }
      else
      {
        // Static method
        var typeName = method.DeclaringType != null ? TypeHelper.TypeNameToString(method.DeclaringType) : "";
        message = (FormattableString)$"ArgumentOutOfRangeException caused by calling {typeName}.{methodName}({argsString}).";
      }

      // Append lambda item context if available
      if (visitor.LambdaItemIndex.HasValue)
      {
        var serializedItem = Serializer.Serialize(visitor.LambdaItem);
        message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{visitor.LambdaItemIndex}] of {visitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
      }

      return new HandledException(message, methodCall);
    }

    private static string BuildArgumentsString(MethodCallExpression methodCall, ArgumentOutOfRangeVisitor visitor)
    {
      var args = new System.Text.StringBuilder();
      var parameters = methodCall.Method.GetParameters();

      for (int i = 0; i < methodCall.Arguments.Count; i++)
      {
        if (i > 0) args.Append(", ");

        var arg = methodCall.Arguments[i];

        if (ExpressionHelper.IsConstantExpression(arg))
        {
          try
          {
            var value = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(arg));
            args.Append(Serializer.Serialize(value));
          }
          catch
          {
            args.Append(ExpressionHelper.ExpressionToString(arg, allowQuotation: false));
          }
        }
        else
        {
          var argString = ExpressionHelper.ExpressionToString(arg, allowQuotation: false);
          try
          {
            var value = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(arg));
            args.Append($"{argString} (value: {Serializer.Serialize(value)})");
          }
          catch
          {
            args.Append(argString);
          }
        }
      }

      return args.ToString();
    }

    private class ArgumentOutOfRangeVisitor : LambdaAwareExpressionVisitor
    {
      public MethodCallExpression? CauseOfException { get; private set; }

      protected override bool HasFoundResult => CauseOfException != null;

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        var result = base.VisitMethodCall(node);

        if (CauseOfException != null)
        {
          return result;
        }

        // Skip indexer access (handled by IndexOutOfRangeExceptionPattern)
        if (node.Method.Name == "get_Item")
        {
          return result;
        }

        // Check if this method call throws ArgumentOutOfRangeException
        if (ThrowsArgumentOutOfRangeException(node))
        {
          CauseOfException = node;
        }

        return result;
      }

      private bool ThrowsArgumentOutOfRangeException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException)
        {
          return true;
        }
        catch (ArgumentOutOfRangeException)
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
