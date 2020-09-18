using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Assertive.Helpers
{
  internal static class Serializer
  {
    private static readonly string[] _newLineSplitChars =
    {
      Environment.NewLine
    };

    private static readonly string[] _spaceSplitChars =
    {
      " "
    };

    public static string Serialize(object? o)
    {
      try
      {
        return SerializeImpl(o, 0, null);
      }
      catch
      {
        return "<exception serializing>";
      }
    }

    private class Ellipsis
    {
      public override string ToString() => "...";
    }

    private static string SerializeImpl(object? o, int indentation, Stack<object>? recursionGuard)
    {
      if (o is null)
      {
        return "null";
      }

      if (o is string s)
      {
        return "\"" + s + "\"";
      }

      if (o is DateTime d)
      {
        return d.ToString("O");
      }

      var type = o.GetType();
      
      if (o is IConvertible c)
      {
        return c.ToString(CultureInfo.InvariantCulture);
      }

      if (type.IsPrimitive)
      {
        return o.ToString();
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
              items.Add(new Ellipsis());
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

          var toStringMethod = o.GetType().GetMethod("ToString", Type.EmptyTypes);

          if (toStringMethod == null
              || (toStringMethod.DeclaringType != typeof(object)
                  && toStringMethod.DeclaringType != typeof(ValueType))
              || properties.Length == 0)
          {
            return o.ToString();
          }

          sb.AppendLine("{");
          indentation++;

          bool firstPropertyWritten = false;

          foreach (var p in properties)
          {
            if (p.GetIndexParameters().Length != 0)
            {
              continue;
            }

            var value = p.GetValue(o);

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

            sb.Append(IndentString($"{p.Name} = {SerializeImpl(value, indentation, recursionGuard)}"));
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