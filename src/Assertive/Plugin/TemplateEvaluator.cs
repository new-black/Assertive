using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Assertive.Plugin
{
  /// <summary>
  /// Evaluates template strings with placeholders like {instance}, {instance.count}, {arg0}.
  /// </summary>
  internal partial class TemplateEvaluator
  {
    private static readonly Regex _placeholderPattern = PlaceholderRegex();

    private readonly Dictionary<string, Func<string?>> _variables = new();

    /// <summary>Registers a placeholder for evaluation.</summary>
    internal void Add(string name, Func<string?> value) => _variables[name] = value;

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

    internal static string FormatValue(object? value)
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

    internal static string FormatTypeName(Type type)
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

    internal static string FormatFirstItems(object? value, int maxItems)
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
