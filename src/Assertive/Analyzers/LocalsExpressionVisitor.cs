using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Assertive.Analyzers
{
  internal class AssertionLocal
  {
    public AssertionLocal(string name, Expression expression)
    {
      Name = name;
      Expression = expression;
    }

    public string Name { get; }
    public Expression Expression { get; }
  }

  internal class LocalsExpressionVisitor : ExpressionVisitor
  {
    public Dictionary<string, AssertionLocal> Locals { get; } = new  Dictionary<string, AssertionLocal>();

    protected override Expression VisitMember(MemberExpression node)
    {
      bool isLocal = false;
      var instance = node.Expression;
      var member = node.Member;

      if (member is FieldInfo { IsPrivate: true })
      {
        isLocal = true;
      }
      else if (member is PropertyInfo propertyInfo)
      {
        var getMethod = propertyInfo.GetGetMethod(true);

        if (getMethod != null && (getMethod.IsPrivate || getMethod.IsFamily))
        {
          isLocal = true;
        }
      }

      if (instance != null)
      {
        if (instance is ConstantExpression constantExpression
            && constantExpression.Value?.GetType().GetCustomAttribute<CompilerGeneratedAttribute>() != null)
        {
          isLocal = true;
        }
        else if (instance is MemberExpression memberExpression
                 && memberExpression.Member.Name.Contains("<"))
        {
          isLocal = true;
        }

        if (!isLocal)
        {
          Visit(instance);
        }
      }
      
      var memberName = member.Name;

      TupleElementNamesAttribute? GetTupleNames()
      {
        if (instance is MemberExpression m)
        {
          return m.Member.GetCustomAttribute<TupleElementNamesAttribute>();
        }

        if (instance is MethodCallExpression c)
        {
          return c.Method.ReturnParameter?.GetCustomAttribute<TupleElementNamesAttribute>();
        }

        return null;
      }

      var tupleNames = GetTupleNames();

      if (tupleNames != null)
      {
        if (int.TryParse(member.Name.Replace("Item", ""), out var tupleElement))
        {
          memberName = tupleNames.TransformNames[tupleElement - 1];
        }
      }

      if (isLocal && memberName != null && !Locals.ContainsKey(memberName))
      {
        Locals.Add(memberName, new AssertionLocal(memberName, node));
      }

      return node;
    }
  }
}