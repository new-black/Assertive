using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Assertive.Helpers
{
  internal class SerializerResult
  {
    private readonly string _value;
    public SerializerResult(string value)
    {
      _value = value;
    }

    public override string ToString()
    {
      var maxLength = Config.Configuration.Output.MaxValueLength;
      var value = _value;

      // Apply truncation if configured
      if (maxLength.HasValue && value.Length > maxLength.Value)
      {
        value = value[..maxLength.Value] + "...";
      }

      if (value.Length < 100)
      {
        return Config.Configuration.Colors.Expression(value);
      }

      return value;
    }
  }
  internal static class Serializer
  {
    private static readonly string[] _newLineSplitChars =
    [
      Environment.NewLine
    ];

    private static readonly string[] _spaceSplitChars =
    [
      " "
    ];

    public static SerializerResult Serialize(object? o)
    {
      try
      {
        var result = SerializeImpl(o, 0, null);

        return new SerializerResult(result);
      }
      catch(Exception ex)
      {
        return new SerializerResult($"<exception serializing = {ex.InnerException?.GetType().Name ?? ex.GetType().Name}>");
      }
    }

    private class Ellipsis
    {
      private readonly int? _remaining;

      public Ellipsis(int? remaining)
      {
        _remaining = remaining;
      }
      public override string ToString() => _remaining.HasValue ? $"... ({_remaining} more items)" : "...";
    }

    private static string SerializeImpl(object? o, int indentation, Stack<object>? recursionGuard)
    {
      if (o is null)
      {
        return "null";
      }

      if (o is string s)
      {
        if (s.StartsWith("<exception serializing")) return s;
        
        return "\"" + s + "\"";
      }

      if (o is DateTime d)
      {
        return d.ToString("O");
      }

      var type = o.GetType();

      // Handle Newtonsoft.Json types by using their ToString() which returns proper JSON
      // We check by namespace to avoid a direct dependency on the Newtonsoft.Json package
      if (type.Namespace == "Newtonsoft.Json.Linq")
      {
        return o.ToString() ?? "";
      }

      // Handle System.Text.Json.Nodes types (JsonObject, JsonArray, JsonValue, JsonNode)
      if (type.Namespace == "System.Text.Json.Nodes")
      {
        return o.ToString() ?? "";
      }

      // Handle System.Text.Json types (JsonElement, JsonDocument)
      if (type.FullName == "System.Text.Json.JsonElement")
      {
        return o.ToString() ?? "";
      }

      if (type.FullName == "System.Text.Json.JsonDocument")
      {
        // JsonDocument doesn't have a useful ToString, so we get the RootElement
        var rootElementProp = type.GetProperty("RootElement");
        if (rootElementProp != null)
        {
          var rootElement = rootElementProp.GetValue(o);
          return rootElement?.ToString() ?? "";
        }
      }

      if (o is IConvertible c)
      {
        return c.ToString(CultureInfo.InvariantCulture);
      }

      if (type.IsPrimitive)
      {
        return o.ToString() ?? "";
      }

      if (o is byte[] bytes)
      {
        var more = false;
        if (bytes.Length > 50)
        {
          more = true;
          var tmp = new byte[50];
          Array.Copy(bytes, tmp, 50);
        }

        return Convert.ToBase64String(bytes) + (more ? "..." : "");
      }
      
      if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
      {
        var keyProperty = type.GetProperty("Key");
        var valueProperty = type.GetProperty("Value");
        
        var key = SerializeImpl(keyProperty!.GetValue(o), indentation, recursionGuard);
        var value = SerializeImpl(valueProperty!.GetValue(o), indentation, recursionGuard);
        
        return $"[{key}] = {value}";
      }

      string IndentString(string str)
      {
        return new string(' ', indentation) + str;
      }

      if (recursionGuard == null)
      {
        recursionGuard = new Stack<object>();
      }

      if (recursionGuard.Contains(o))
      {
        return "<infinite recursion>";
      }

      recursionGuard.Push(o);

      try
      {
        var sb = new StringBuilder();

        if (TypeHelper.IsEnumerable(type))
        {
          var isDict = TypeHelper.IsDictionary(type);

          var items = new List<object>();

          foreach (var v in (IEnumerable)o)
          {
            if (items.Count == 10)
            {
              int? remaining = null;

              if (o is TruncatedList truncatedList)
              {
                remaining = truncatedList.OriginalCount - 10;
              }
              else if (o is IList list)
              {
                remaining = list.Count - 10;
                if (remaining <= 0)
                {
                  remaining = null;
                }
              }
              
              items.Add(new Ellipsis(remaining));
              break;
            }

            items.Add(v);
          }

          sb.AppendLine(isDict ? "{" : "[");
          indentation++;

          for (var i = 0; i < items.Count; i++)
          {
            var item = items[i];

            sb.Append(IndentString(SerializeImpl(item, indentation, recursionGuard)));

            if (i == items.Count - 1)
            {
              sb.AppendLine();
            }
            else
            {
              sb.AppendLine(",");
            }
          }

          indentation--;
          sb.Append(IndentString(isDict ? "}" : "]"));
        }
        else
        {
          var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead).ToArray();

          var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).ToArray();

          var toStringMethod = o.GetType().GetMethod("ToString", Type.EmptyTypes);

          var members = new List<MemberInfo>();
          
          members.AddRange(properties);
          members.AddRange(fields);

          if (toStringMethod == null
              || (toStringMethod.DeclaringType != typeof(object)
                  && toStringMethod.DeclaringType != typeof(ValueType))
              || members.Count == 0)
          {
            return o.ToString() ?? "";
          }

          sb.AppendLine("{");
          indentation++;

          bool firstPropertyWritten = false;

          foreach (var m in members)
          {
            var p = m as PropertyInfo;
            var f = m as FieldInfo;
            
            if (p != null && p.GetIndexParameters().Length != 0)
            {
              continue;
            }

            object? value = null;
            try
            {
              if (p != null)
              {
                value = p.GetValue(o);
              }
              else
              {
                value = f!.GetValue(o);
              }
            }
            catch(Exception ex)
            {
              value = $"<exception serializing = {ex.InnerException?.GetType().Name ?? ex.GetType().Name}>";
            }

            if (value == null)
            {
              continue;
            }

            if (firstPropertyWritten)
            {
              sb.AppendLine(",");
            }
            else
            {
              firstPropertyWritten = true;
            }

            var name = p != null ? p.Name : f!.Name;

            sb.Append(IndentString($"{name} = {SerializeImpl(value, indentation, recursionGuard)}"));
          }

          sb.AppendLine();
          indentation--;
          sb.Append(IndentString("}"));
        }

        var result = sb.ToString();

        if (result.Length < 150)
        {
          result = string.Join(" ", string.Join(" ", result.Split(_newLineSplitChars, StringSplitOptions.None))
            .Split(_spaceSplitChars, StringSplitOptions.RemoveEmptyEntries));
        }

        return result;
      }
      finally
      {
        recursionGuard.Pop();
      }
    }
  }
}