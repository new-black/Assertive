// Based on https://raw.githubusercontent.com/dotnet/runtime/4f9ae42d861fcb4be2fcd5d3d55d5f227d30e723/src/libraries/System.Linq.Expressions/src/System/Linq/Expressions/ExpressionStringBuilder.cs
// Licensed under MIT.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Assertive.Helpers;

namespace Assertive.Expressions
{
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
  internal sealed class ExpressionStringBuilder : ExpressionVisitor
  {
    private readonly StringBuilder _out;

    public bool EmitMethodCallWithoutObject { get; set; }

    private ExpressionStringBuilder()
    {
      _out = new StringBuilder();
    }

    public override string ToString()
    {
      return _out.ToString();
    }

    #region The printing code

    private void Out(string? s)
    {
      _out.Append(s);
    }

    private void Out(char c)
    {
      _out.Append(c);
    }

    #endregion

    #region Output an expression tree to a string

    /// <summary>
    /// Output a given expression tree to a string.
    /// </summary>
    internal static string ExpressionToString(Expression node)
    {
      Debug.Assert(node != null);
      ExpressionStringBuilder esb = new ExpressionStringBuilder();
      esb.Visit(node!);
      return esb.ToString();
    }

    internal static string MethodCallToString(MethodCallExpression node)
    {
      ExpressionStringBuilder esb = new ExpressionStringBuilder();
      esb.EmitMethodCallWithoutObject = true;
      esb.Visit(node);
      return esb.ToString();
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
      Out(node.Name);
      return node;
    }

    private void VisitExpressions<T>(char open, ReadOnlyCollection<T>? expressions, char close, string seperator = ", ")
      where T : Expression
    {
      Out(open);
      if (expressions != null)
      {
        bool isFirst = true;
        foreach (T e in expressions)
        {
          if (isFirst)
          {
            isFirst = false;
          }
          else
          {
            Out(seperator);
          }

          Visit(e);
        }
      }

      Out(close);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
    protected override Expression VisitBinary(BinaryExpression node)
    {
      if (node.NodeType == ExpressionType.ArrayIndex)
      {
        Visit(node.Left);
        Out('[');
        Visit(node.Right);
        Out(']');
      }
      else
      {
        var op = node.NodeType switch
        {
          ExpressionType.AndAlso => "&&",
          ExpressionType.OrElse => "||",
          ExpressionType.Assign => "=",
          ExpressionType.Equal => "==",
          ExpressionType.NotEqual => "!=",
          ExpressionType.GreaterThan => ">",
          ExpressionType.LessThan => "<",
          ExpressionType.GreaterThanOrEqual => ">=",
          ExpressionType.LessThanOrEqual => "<=",
          ExpressionType.Add => "+",
          ExpressionType.AddChecked => "+",
          ExpressionType.AddAssign => "+=",
          ExpressionType.AddAssignChecked => "+=",
          ExpressionType.Subtract => "-",
          ExpressionType.SubtractChecked => "-",
          ExpressionType.SubtractAssign => "-=",
          ExpressionType.SubtractAssignChecked => "-=",
          ExpressionType.Divide => "/",
          ExpressionType.DivideAssign => "/=",
          ExpressionType.Modulo => "%",
          ExpressionType.ModuloAssign => "%=",
          ExpressionType.Multiply => "*",
          ExpressionType.MultiplyChecked => "*",
          ExpressionType.MultiplyAssign => "*=",
          ExpressionType.MultiplyAssignChecked => "*=",
          ExpressionType.LeftShift => "<<",
          ExpressionType.LeftShiftAssign => "<<=",
          ExpressionType.RightShift => ">>",
          ExpressionType.RightShiftAssign => ">>=",
          ExpressionType.And => "&",
          ExpressionType.AndAssign => "&=",
          ExpressionType.Or => "|",
          ExpressionType.OrAssign => "||=",
          ExpressionType.ExclusiveOr => "^",
          ExpressionType.ExclusiveOrAssign => "^=",
          ExpressionType.Power => "**",
          ExpressionType.PowerAssign => "**=",
          ExpressionType.Coalesce => "??",
          _ => throw new InvalidOperationException()
        };

        //Out('(');
        Visit(node.Left);
        Out(' ');
        Out(op);
        Out(' ');
        Visit(node.Right);
        //Out(')');
      }

      return node;
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
      if (node.Parameters.Count == 1)
      {
        // p => body
        Visit(node.Parameters[0]);
      }
      else
      {
        // (p1, p2, ..., pn) => body
        Out('(');
        string sep = ", ";
        for (int i = 0, n = node.Parameters.Count; i < n; i++)
        {
          if (i > 0)
          {
            Out(sep);
          }

          Visit(node.Parameters[i]);
        }

        Out(')');
      }

      Out(" => ");
      Visit(node.Body);
      return node;
    }

    protected override Expression VisitListInit(ListInitExpression node)
    {
      Visit(node.NewExpression);
      Out(" { ");
      for (int i = 0, n = node.Initializers.Count; i < n; i++)
      {
        if (i > 0)
        {
          Out(", ");
        }

        VisitElementInit(node.Initializers[i]);
      }

      Out(" }");
      return node;
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
      Visit(node.Test);
      Out(" ? ");
      Visit(node.IfTrue);
      Out(" : ");
      Visit(node.IfFalse);

      return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
      if (node.Value != null)
      {
        string? sValue = node.Value?.ToString();
        if (node.Value is string)
        {
          Out('\"');
          Out(sValue);
          Out('\"');
        }
        else if (node.Type.IsEnum)
        {
          Out(node.Type.Name);
          Out(".");
          Out(sValue);
        }
        else
        {
          if (node.Value is bool)
          {
            sValue = sValue?.ToLower();
          }

          Out(sValue);
        }
      }
      else
      {
        Out("null");
      }

      return node;
    }

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
      else if (!isLocal)
      {
        // For static members, include the type name
        Out(member.DeclaringType?.Name);
      }

      if (!isLocal)
      {
        Out('.');
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

      Out(memberName);


      return node;
    }

    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
      if (node.NewExpression.Arguments.Count == 0 &&
          node.NewExpression.Type.Name.Contains('<'))
      {
        // anonymous type constructor
        Out("new");
      }
      else
      {
        Visit(node.NewExpression);
      }

      Out(" {");
      for (int i = 0, n = node.Bindings.Count; i < n; i++)
      {
        MemberBinding b = node.Bindings[i];
        if (i > 0)
        {
          Out(", ");
        }

        VisitMemberBinding(b);
      }

      Out('}');
      return node;
    }

    protected override MemberAssignment VisitMemberAssignment(MemberAssignment assignment)
    {
      Out(assignment.Member.Name);
      Out(" = ");
      Visit(assignment.Expression);
      return assignment;
    }

    protected override MemberListBinding VisitMemberListBinding(MemberListBinding binding)
    {
      Out(binding.Member.Name);
      Out(" = {");
      for (int i = 0, n = binding.Initializers.Count; i < n; i++)
      {
        if (i > 0)
        {
          Out(", ");
        }

        VisitElementInit(binding.Initializers[i]);
      }

      Out('}');
      return binding;
    }

    protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding binding)
    {
      Out(binding.Member.Name);
      Out(" = {");
      for (int i = 0, n = binding.Bindings.Count; i < n; i++)
      {
        if (i > 0)
        {
          Out(", ");
        }

        VisitMemberBinding(binding.Bindings[i]);
      }

      Out('}');
      return binding;
    }

    protected override ElementInit VisitElementInit(ElementInit initializer)
    {
      string sep = ", ";

      for (int i = 0, n = initializer.Arguments.Count; i < n; i++)
      {
        if (i > 0)
        {
          Out(sep);
        }

        Visit(initializer.Arguments[i]);
      }

      return initializer;
    }

    protected override Expression VisitInvocation(InvocationExpression node)
    {
      Out("Invoke(");
      Visit(node.Expression);
      string sep = ", ";
      for (int i = 0, n = node.Arguments.Count; i < n; i++)
      {
        Out(sep);
        Visit(node.Arguments[i]);
      }

      Out(')');
      return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
      int start = 0;
      Expression? ob = node.Object;

      if (node.Method.GetCustomAttribute(typeof(ExtensionAttribute)) != null)
      {
        start = 1;
        ob = node.Arguments[0];
      }

      var isIndexer = node.Method.Name == "get_Item";

      if (ob != null && !node.Method.IsPrivate)
      {
        if (!EmitMethodCallWithoutObject)
        {
          Visit(ob);
          if (!isIndexer)
          {
            Out('.');
          }
        }
      }
      else if (node.Method is { IsStatic: true, IsPublic: true })
      {
        Out(TypeHelper.TypeNameToString(node.Method.DeclaringType));

        if (!isIndexer)
        {
          Out('.');
        }
      }

      if (isIndexer)
      {
        Out('[');
      }
      else
      {
        Out(node.Method.Name);
        Out('(');
      }

      for (int i = start, n = node.Arguments.Count; i < n; i++)
      {
        if (i > start)
          Out(", ");
        Visit(node.Arguments[i]);
      }

      Out(isIndexer ? ']' : ')');

      return node;
    }

    protected override Expression VisitNewArray(NewArrayExpression node)
    {
      string ArrayElementType(Type t)
      {
        if (!t.IsArray) return TypeHelper.TypeNameToString(t);

        var elementType = t.GetElementType();

        if (elementType?.IsArray == true)
        {
          return ArrayElementType(elementType);
        }
        else
        {
          return TypeHelper.TypeNameToString(elementType);
        }
      }

      void Multidimensional(Type t)
      {
        if (t.IsArray && t.GetElementType()!.IsArray)
        {
          Out("[]");
          Multidimensional(t.GetElementType()!);
        }
      }
      
      switch (node.NodeType)
      {
        case ExpressionType.NewArrayBounds:
          // new MyType[](expr1, expr2)
          Out("new ");
          Out(ArrayElementType(node.Type));
          VisitExpressions('[', node.Expressions, ']');
          Multidimensional(node.Type);
          
          break;
        case ExpressionType.NewArrayInit:
          // new [] {expr1, expr2}
          Out("new [] ");
          VisitExpressions('{', node.Expressions, '}');
          break;
      }

      return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
      var isAnonymous = node.Type.GetCustomAttribute<CompilerGeneratedAttribute>() != null;

      Out("new ");

      string TypeToString(Type t)
      {
        if (t.IsGenericType)
        {
          var baseName = t.Name.Substring(0, t.Name.IndexOf('`'));

          return $"{baseName}<{string.Join(", ", t.GetGenericArguments().Select(TypeToString))}>";
        }
        else
        {
          return TypeHelper.TypeNameToString(t);
        }
      }

      if (!isAnonymous)
      {
        Out(TypeToString(node.Type));
      }

      Out(isAnonymous ? "{ " : "(");
      ReadOnlyCollection<MemberInfo>? members = node.Members;
      for (int i = 0; i < node.Arguments.Count; i++)
      {
        if (i > 0)
        {
          Out(", ");
        }

        if (members != null)
        {
          string name = members[i].Name;
          Out(name);
          Out(" = ");
        }

        Visit(node.Arguments[i]);
      }

      Out(isAnonymous ? " }" : ")");
      return node;
    }

    protected override Expression VisitTypeBinary(TypeBinaryExpression node)
    {
      Out('(');
      Visit(node.Expression);
      switch (node.NodeType)
      {
        case ExpressionType.TypeIs:
          Out(" is ");
          break;
        case ExpressionType.TypeEqual:
          Out(" TypeEqual ");
          break;
      }

      Out(node.TypeOperand.Name);
      Out(')');
      return node;
    }

    private static bool IsNullableValueType(Type type)
    {
      return (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
    }

    private Expression? VisitConvert(UnaryExpression node)
    {
      if ((node.Type.IsNullableValueType()
           && node.Type.GetUnderlyingType() == node.Operand.Type) || node.Type == typeof(object))
      {
        return Visit(node.Operand);
      }
      else
      {
        Out('(');
        Out(TypeHelper.TypeNameToString(node.Type));
        Out(')');
        return Visit(node.Operand);
      }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
    protected override Expression VisitUnary(UnaryExpression node)
    {
      if (node.NodeType == ExpressionType.Convert)
      {
        return VisitConvert(node) ?? node;
      }

      var parentheses = false;

      switch (node.NodeType)
      {
        case ExpressionType.Negate:
        case ExpressionType.NegateChecked:
          Out('-');
          break;
        case ExpressionType.Not:
          Out("!");

          if (node.Operand is BinaryExpression)
          {
            parentheses = true;
          }

          break;
        case ExpressionType.IsFalse:
          Out("IsFalse(");
          break;
        case ExpressionType.IsTrue:
          Out("IsTrue(");
          break;
        case ExpressionType.OnesComplement:
          Out("~(");
          break;
        case ExpressionType.ArrayLength:
          break;
        case ExpressionType.ConvertChecked:
          Out("ConvertChecked(");
          break;
        case ExpressionType.Throw:
          Out("throw(");
          break;
        case ExpressionType.TypeAs:
          Out('(');
          break;
        case ExpressionType.UnaryPlus:
          Out('+');
          break;
        case ExpressionType.Unbox:
          Out("Unbox(");
          break;
        case ExpressionType.Increment:
          Out("Increment(");
          break;
        case ExpressionType.Decrement:
          Out("Decrement(");
          break;
        case ExpressionType.PreIncrementAssign:
          Out("++");
          break;
        case ExpressionType.PreDecrementAssign:
          Out("--");
          break;
        case ExpressionType.Quote:
        case ExpressionType.PostIncrementAssign:
        case ExpressionType.PostDecrementAssign:
          break;
        default:
          throw new InvalidOperationException();
      }

      if (parentheses)
      {
        Out('(');
      }

      Visit(node.Operand);

      if (parentheses)
      {
        Out(')');
      }

      switch (node.NodeType)
      {
        case ExpressionType.Negate:
        case ExpressionType.NegateChecked:
        case ExpressionType.UnaryPlus:
        case ExpressionType.PreDecrementAssign:
        case ExpressionType.PreIncrementAssign:
        case ExpressionType.Quote:
        case ExpressionType.Not:
          break;
        case ExpressionType.ArrayLength:
          Out(".Length");
          break;
        case ExpressionType.TypeAs:
          Out(" as ");
          Out(TypeHelper.TypeNameToString(node.Type));
          Out(')');
          break;
        case ExpressionType.ConvertChecked:
          break;
        case ExpressionType.PostIncrementAssign:
          Out("++");
          break;
        case ExpressionType.PostDecrementAssign:
          Out("--");
          break;
        default:
          Out(')');
          break;
      }

      return node;
    }

    protected override Expression VisitDefault(DefaultExpression node)
    {
      Out("default(");
      Out(TypeHelper.TypeNameToString(node.Type));
      Out(')');
      return node;
    }

    protected override Expression VisitIndex(IndexExpression node)
    {
      if (node.Object != null)
      {
        Visit(node.Object);
      }
      else
      {
        Debug.Assert(node.Indexer != null);
        Out(node.Indexer!.DeclaringType!.Name);
      }

      if (node.Indexer != null)
      {
        Out('.');
        Out(node.Indexer.Name);
      }

      Out('[');
      for (int i = 0, n = node.Arguments.Count; i < n; i++)
      {
        if (i > 0)
          Out(", ");
        Visit(node.Arguments[i]);
      }

      Out(']');

      return node;
    }

    protected override Expression VisitExtension(Expression node)
    {
      // Prefer an overridden ToString, if available.
      MethodInfo toString = node.GetType().GetMethod("ToString", Type.EmptyTypes)!;
      if (toString.DeclaringType != typeof(Expression) && !toString.IsStatic)
      {
        Out(node.ToString());
        return node;
      }

      Out('[');
      // For 3.5 subclasses, print the NodeType.
      // For Extension nodes, print the class name.
      Out(node.NodeType == ExpressionType.Extension ? node.GetType().FullName : node.NodeType.ToString());
      Out(']');
      return node;
    }

    public override Expression? Visit(Expression? node)
    {
      if (node is NamedConstantExpression namedConstantExpression)
      {
        Out(namedConstantExpression.Name);
        return node;
      }

      return base.Visit(node);
    }

    #endregion
  }
}