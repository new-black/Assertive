using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Assertive.Helpers
{
  internal static class TypeHelper
  {
    public static bool IsNullableValueType(this Type type)
    {
      return (type.IsGenericType && type.
                GetGenericTypeDefinition() == typeof(Nullable<>));
    }
    
    public static bool IsType<TBase>(this Type t)
    {
      return typeof(TBase).IsAssignableFrom(t);
    }
    
    public static bool IsType(this Type t, Type t2)
    {
      return t2.IsAssignableFrom(t);
    }
    
    public static Type GetUnderlyingType(this Type type)
    {
      if (IsNullableValueType(type))
      {
        return Nullable.GetUnderlyingType(type)!;
      }
      return type;
    }

    public static bool IsDictionary(Type t)
    {
      if (t.IsType<IDictionary>()) return true;

      return t.IsGenericType && t.GetGenericTypeDefinition().IsType(typeof(IDictionary<,>));
    }

    public static bool IsEnumerable(Type t)
    {
      return typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string);
    }
    
    public static Type? GetTypeInsideEnumerable(Type type)
    {
      var getEnumeratorMethod = type.GetMethod("GetEnumerator", Type.EmptyTypes);

      if (getEnumeratorMethod == null)
      {
        getEnumeratorMethod = (from i in type.GetInterfaces()
          from m in i.GetMethods()
          where m.Name == "GetEnumerator"
          orderby m.ReturnType.IsGenericType descending
          select m).FirstOrDefault();

      }

      if (getEnumeratorMethod == null) return null;

      if (getEnumeratorMethod.ReturnType.IsGenericType)
      {
        var args = getEnumeratorMethod.ReturnType.GetGenericArguments();

        if (IsDictionary(type) && args.Length == 2)
        {
          return typeof(KeyValuePair<,>).MakeGenericType(args[0], args[1]);
        }

        return args.First();
      }
      else if (type.IsArray)
      {
        return type.GetElementType();
      }

      return typeof(object);

    }
    
    public static string TypeNameToString(Type? t)
    {
      if (t == null) return "";
      if (t == typeof(bool))
      {
        return "bool";
      }

      if (t == typeof(byte))
      {
        return "byte";
      }

      if (t == typeof(sbyte))
      {
        return "sbyte";
      }

      if (t == typeof(char))
      {
        return "char";
      }

      if (t == typeof(decimal))
      {
        return "decimal";
      }

      if (t == typeof(double))
      {
        return "double";
      }

      if (t == typeof(float))
      {
        return "float";
      }

      if (t == typeof(int))
      {
        return "int";
      }

      if (t == typeof(uint))
      {
        return "uint";
      }

      if (t == typeof(long))
      {
        return "long";
      }

      if (t == typeof(ulong))
      {
        return "ulong";
      }

      if (t == typeof(object))
      {
        return "object";
      }

      if (t == typeof(short))
      {
        return "short";
      }

      if (t == typeof(ushort))
      {
        return "ushort";
      }

      if (t == typeof(string))
      {
        return "string";
      }

      if (IsNullableValueType(t))
      {
        return TypeNameToString(Nullable.GetUnderlyingType(t)) + "?";
      }

      return t.Name;
    }

  }
}