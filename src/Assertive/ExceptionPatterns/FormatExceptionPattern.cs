using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class FormatExceptionPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is FormatException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var visitor = new FormatExceptionVisitor();

      visitor.Visit(assertion.Expression);

      if (visitor.CauseOfFormatException == null)
      {
        return null;
      }

      var methodCall = visitor.CauseOfFormatException;
      var method = methodCall.Method;
      var methodName = method.Name;
      var typeName = method.DeclaringType != null ? TypeHelper.TypeNameToString(method.DeclaringType) : "";

      // Try to get the string argument that failed to parse
      string? inputValue = null;
      Expression? inputExpression = null;

      // Find the string argument (usually the first argument)
      foreach (var arg in methodCall.Arguments)
      {
        if (arg.Type == typeof(string))
        {
          inputExpression = arg;
          try
          {
            var value = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(arg));
            inputValue = value as string;
          }
          catch
          {
            // Could not evaluate
          }
          break;
        }
      }

      string inputString;
      if (inputValue != null)
      {
        inputString = Serializer.Serialize(inputValue).ToString();
      }
      else if (inputExpression != null)
      {
        inputString = ExpressionHelper.ExpressionToString(inputExpression, allowQuotation: false);
      }
      else
      {
        inputString = "unknown";
      }

      FormattableString message;

      if (method.IsStatic)
      {
        message = (FormattableString)$"FormatException caused by calling {typeName}.{methodName}({inputString}). {inputString} is not a valid {GetTargetTypeName(method)}.";
      }
      else
      {
        var instance = methodCall.Object;
        var instanceString = instance != null
          ? ExpressionHelper.ExpressionToString(instance, allowQuotation: false)
          : "";
        message = (FormattableString)$"FormatException caused by calling {methodName}({inputString}) on {instanceString}.";
      }

      // Append lambda item context if available
      if (visitor.LambdaItemIndex.HasValue)
      {
        var serializedItem = Serializer.Serialize(visitor.LambdaItem);
        message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{visitor.LambdaItemIndex}] of {visitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
      }

      return new HandledException(message, methodCall);
    }

    private static string GetTargetTypeName(MethodInfo method)
    {
      // For Parse methods, the target type is the declaring type
      if (method.Name == "Parse" || method.Name == "TryParse")
      {
        return TypeHelper.TypeNameToString(method.DeclaringType);
      }

      // For Convert.ToXxx methods, extract the type from the method name
      if (method.DeclaringType == typeof(Convert) && method.Name.StartsWith("To"))
      {
        var typePart = method.Name.Substring(2);
        return typePart switch
        {
          "Int32" => "int",
          "Int64" => "long",
          "Int16" => "short",
          "Double" => "double",
          "Single" => "float",
          "Decimal" => "decimal",
          "Boolean" => "bool",
          "Byte" => "byte",
          "DateTime" => "DateTime",
          _ => typePart
        };
      }

      return method.ReturnType != typeof(void)
        ? TypeHelper.TypeNameToString(method.ReturnType)
        : "value";
    }

    private class FormatExceptionVisitor : LambdaAwareExpressionVisitor
    {
      public MethodCallExpression? CauseOfFormatException { get; private set; }

      protected override bool HasFoundResult => CauseOfFormatException != null;

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        var result = base.VisitMethodCall(node);

        if (CauseOfFormatException != null)
        {
          return result;
        }

        // Check if this method call throws FormatException
        if (IsParsingMethod(node.Method) && ThrowsFormatException(node))
        {
          CauseOfFormatException = node;
        }

        return result;
      }

      private static bool IsParsingMethod(MethodInfo method)
      {
        // Common parsing methods
        if (method.Name is "Parse" or "TryParse")
        {
          return true;
        }

        // Convert.ToXxx methods
        if (method.DeclaringType == typeof(Convert) && method.Name.StartsWith("To"))
        {
          return true;
        }

        return false;
      }

      private bool ThrowsFormatException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is FormatException)
        {
          return true;
        }
        catch (FormatException)
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
