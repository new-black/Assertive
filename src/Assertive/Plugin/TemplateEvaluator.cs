using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Assertive.Expressions;

namespace Assertive.Plugin
{
  /// <summary>
  /// Evaluates template strings with placeholders like {instance}, {instance.count}, {arg0}.
  /// </summary>
  internal partial class TemplateEvaluator
  {
    private static readonly Regex _placeholderPattern = PlaceholderRegex();

    private readonly Dictionary<string, Func<string?>> _variables = new();

    /// <summary>
    /// Creates a template evaluator for a method call expression.
    /// </summary>
    public static TemplateEvaluator ForMethodCall(MethodCallExpression methodCall)
    {
      var evaluator = new TemplateEvaluator();

      var instance = ExpressionHelper.GetInstanceOfMethodCall(methodCall);

      if (instance != null)
      {
        AddInstanceVariables(evaluator, instance);
      }

      // {method} - the method name
      evaluator._variables["method"] = () => methodCall.Method.Name;

      // Check if this is an extension method
      var isExtensionMethod = methodCall.Method.IsDefined(typeof(ExtensionAttribute), false);

      // Add arguments: {arg0}, {arg0.value}, {arg0.type}, {arg1}, etc.
      for (int i = 0; i < methodCall.Arguments.Count; i++)
      {
        var arg = methodCall.Arguments[i];

        // For extension methods, arg0 is the instance, so skip it and adjust indices
        // For regular static methods, all arguments are available starting at arg0
        int argIndex;
        if (isExtensionMethod && methodCall.Object == null)
        {
          if (i == 0)
          {
            continue; // Skip first arg (instance) for extension methods
          }

          argIndex = i - 1;
        }
        else
        {
          argIndex = i;
        }

        evaluator._variables[$"arg{argIndex}"] = () => ExpressionHelper.ExpressionToString(arg);
        evaluator._variables[$"arg{argIndex}.value"] = () => FormatValue(ExpressionHelper.EvaluateExpression(arg));
        evaluator._variables[$"arg{argIndex}.type"] = () => FormatTypeName(arg.Type);
      }

      return evaluator;
    }

    /// <summary>
    /// Creates a template evaluator for a property access expression.
    /// </summary>
    public static TemplateEvaluator ForPropertyAccess(MemberExpression memberExpr)
    {
      var evaluator = new TemplateEvaluator();

      var instance = memberExpr.Expression;

      if (instance != null)
      {
        AddInstanceVariables(evaluator, instance);
      }

      // {property} - the property name
      evaluator._variables["property"] = () => memberExpr.Member.Name;

      // {value} - the property value
      evaluator._variables["value"] = () => FormatValue(ExpressionHelper.EvaluateExpression(memberExpr));

      return evaluator;
    }

    private static void AddInstanceVariables(TemplateEvaluator evaluator, Expression instance)
    {
      // {instance} - the expression as a string
      evaluator._variables["instance"] = () => ExpressionHelper.ExpressionToString(instance);

      // {instance.value} - the evaluated value
      evaluator._variables["instance.value"] = () => FormatValue(ExpressionHelper.EvaluateExpression(instance));

      // {instance.type} - the type of the instance
      evaluator._variables["instance.type"] = () => FormatTypeName(instance.Type);

      // {instance.count} - count for collections
      evaluator._variables["instance.count"] = () =>
      {
        var count = ExpressionHelper.GetCollectionItemCount(instance);
        return count?.ToString() ?? "?";
      };

      // {instance.firstTenItems} - first 10 items of a collection
      evaluator._variables["instance.firstTenItems"] = () => FormatFirstItems(ExpressionHelper.EvaluateExpression(instance), 10);
    }

    /// <summary>
    /// Evaluates a template string, replacing placeholders with their values.
    /// </summary>
    public string Evaluate(string template)
    {
      return _placeholderPattern.Replace(template, match =>
      {
        var placeholder = match.Groups[1].Value;

        if (_variables.TryGetValue(placeholder, out var valueFunc))
        {
          try
          {
            return valueFunc() ?? "";
          }
          catch
          {
            return $"{{{placeholder}}}";
          }
        }

        // Unknown placeholder - leave as-is
        return match.Value;
      });
    }

    /// <summary>
    /// Evaluates a template and returns it as a FormattableString.
    /// </summary>
    public FormattableString EvaluateToFormattable(string template)
    {
      var result = Evaluate(template);
      return $"{result}";
    }

    private static string FormatValue(object? value)
    {
      if (value == null)
      {
        return "null";
      }

      if (value is string s)
      {
        return $"\"{s}\"";
      }

      // Use InvariantCulture for numeric types to ensure consistent decimal separators
      if (value is IConvertible convertible)
      {
        return convertible.ToString(CultureInfo.InvariantCulture);
      }

      return value.ToString() ?? "";
    }

    private static string FormatTypeName(Type type)
    {
      if (!type.IsGenericType)
      {
        return type.Name;
      }

      // Format generic types nicely (e.g., List<String> instead of List`1)
      var genericName = type.Name;
      var backtickIndex = genericName.IndexOf('`');
      if (backtickIndex > 0)
      {
        genericName = genericName[..backtickIndex];
      }

      var genericArgs = type.GetGenericArguments();
      var argNames = new string[genericArgs.Length];
      for (int i = 0; i < genericArgs.Length; i++)
      {
        argNames[i] = FormatTypeName(genericArgs[i]);
      }

      return $"{genericName}<{string.Join(", ", argNames)}>";
    }

    private static string FormatFirstItems(object? value, int maxItems)
    {
      if (value == null)
      {
        return "null";
      }

      if (value is not IEnumerable enumerable)
      {
        return FormatValue(value);
      }

      // Don't enumerate strings as chars
      if (value is string s)
      {
        return $"\"{s}\"";
      }

      var items = new List<string>();
      var count = 0;
      var hasMore = false;

      foreach (var item in enumerable)
      {
        if (count >= maxItems)
        {
          hasMore = true;
          break;
        }
        items.Add(FormatValue(item));
        count++;
      }

      var result = "[" + string.Join(", ", items) + "]";
      if (hasMore)
      {
        result += " ...";
      }

      return result;
    }

        [GeneratedRegex(@"\{([a-zA-Z0-9_.]+)\}", RegexOptions.Compiled)]
        private static partial Regex PlaceholderRegex();
    }
}
