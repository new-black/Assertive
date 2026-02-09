using System;
using System.Linq;
using System.Reflection;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test;

public partial class PublicApiTests
{
  [Fact]
  public void Public_api_surface()
  {
    var assembly = typeof(DSL).Assembly;

    var api = assembly.GetExportedTypes()
      .OrderBy(t => t.FullName)
      .Select(t => FormatType(t))
      .ToList();

    var surface = string.Join("\n\n", api);

    Assert(surface);
  }

  private static string FormatType(Type type)
  {
    var kind = type.IsEnum ? "enum"
      : type.IsInterface ? "interface"
      : type.IsValueType ? "struct"
      : type.BaseType == typeof(MulticastDelegate) ? "delegate"
      : "class";

    var modifier = type.IsAbstract && type.IsSealed ? "static "
      : type.IsAbstract && !type.IsInterface ? "abstract "
      : type.IsSealed && kind == "class" ? "sealed "
      : "";

    var header = $"{modifier}{kind} {FormatTypeName(type)}";

    if (kind == "delegate")
    {
      var invoke = type.GetMethod("Invoke")!;
      var parameters = string.Join(", ", invoke.GetParameters().Select(p => FormatParameter(p)));
      return $"delegate {FormatTypeName(invoke.ReturnType)} {FormatTypeName(type)}({parameters})";
    }

    if (kind == "enum")
    {
      var values = Enum.GetNames(type);
      var members = string.Join("\n", values.Select(v => $"  {v}"));
      return $"{header}\n{members}";
    }

    var lines = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
      .Where(m => m is not Type)
      .Where(m => m.MemberType != MemberTypes.Constructor || !type.IsAbstract)
      .OrderBy(m => m.MemberType switch
      {
        MemberTypes.Constructor => 0,
        MemberTypes.Property => 1,
        MemberTypes.Method => 2,
        MemberTypes.Field => 3,
        MemberTypes.Event => 4,
        _ => 5
      })
      .ThenBy(m => m.Name)
      .Select(m => "  " + FormatMember(m))
      .Where(m => m.Trim().Length > 0)
      .ToList();

    if (lines.Count == 0)
    {
      return header;
    }

    return header + "\n" + string.Join("\n", lines);
  }

  private static string FormatMember(MemberInfo member)
  {
    return member switch
    {
      ConstructorInfo c => FormatConstructor(c),
      PropertyInfo p => FormatProperty(p),
      MethodInfo m when !m.IsSpecialName => FormatMethod(m),
      FieldInfo f => FormatField(f),
      EventInfo e => FormatEvent(e),
      _ => ""
    };
  }

  private static string FormatConstructor(ConstructorInfo c)
  {
    var parameters = string.Join(", ", c.GetParameters().Select(p => FormatParameter(p)));
    var modifier = c.IsStatic ? "static " : "";
    return $"{modifier}.ctor({parameters})";
  }

  private static string FormatProperty(PropertyInfo p)
  {
    var isStatic = (p.GetMethod ?? p.SetMethod)!.IsStatic;
    var modifier = isStatic ? "static " : "";
    var accessors = new[] {
      p.GetMethod != null && p.GetMethod.IsPublic ? "get" : null,
      p.SetMethod != null && p.SetMethod.IsPublic ? "set" : null
    }.Where(a => a != null);
    return $"{modifier}{FormatTypeName(p.PropertyType)} {p.Name} {{ {string.Join("; ", accessors)}; }}";
  }

  private static string FormatMethod(MethodInfo m)
  {
    var modifier = m.IsStatic ? "static " : "";
    var genericArgs = m.IsGenericMethod
      ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">"
      : "";
    var parameters = string.Join(", ", m.GetParameters().Select(p => FormatParameter(p)));
    return $"{modifier}{FormatTypeName(m.ReturnType)} {m.Name}{genericArgs}({parameters})";
  }

  private static string FormatField(FieldInfo f)
  {
    var modifier = f.IsStatic ? "static " : "";
    var constant = f.IsLiteral ? "const " : f.IsInitOnly ? "readonly " : "";
    return $"{modifier}{constant}{FormatTypeName(f.FieldType)} {f.Name}";
  }

  private static string FormatEvent(EventInfo e)
  {
    return $"event {FormatTypeName(e.EventHandlerType!)} {e.Name}";
  }

  private static string FormatParameter(ParameterInfo p)
  {
    return $"{FormatTypeName(p.ParameterType)} {p.Name}";
  }

  private static string FormatTypeName(Type type)
  {
    if (type == typeof(void)) return "void";
    if (type == typeof(string)) return "string";
    if (type == typeof(bool)) return "bool";
    if (type == typeof(int)) return "int";
    if (type == typeof(long)) return "long";
    if (type == typeof(double)) return "double";
    if (type == typeof(float)) return "float";
    if (type == typeof(decimal)) return "decimal";
    if (type == typeof(object)) return "object";
    if (type == typeof(char)) return "char";
    if (type == typeof(byte)) return "byte";

    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
    {
      return FormatTypeName(type.GetGenericArguments()[0]) + "?";
    }

    if (type.IsByRef)
    {
      return "ref " + FormatTypeName(type.GetElementType()!);
    }

    if (type.IsArray)
    {
      return FormatTypeName(type.GetElementType()!) + "[]";
    }

    if (type.IsGenericType)
    {
      var name = type.Name.Split('`')[0];
      var args = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
      return $"{name}<{args}>";
    }

    return type.Name;
  }
}
