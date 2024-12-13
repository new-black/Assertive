using System;
using System.Linq.Expressions;
using Assertive.Helpers;

namespace Assertive.Expressions
{
  internal class ExpressionRewriter : ExpressionVisitor
  {
#pragma warning disable CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    protected override Expression? VisitUnary(UnaryExpression node)
#pragma warning restore CS8764 // Nullability of return type doesn't match overridden member (possibly because of nullability attributes).
    {
      if (IsConversionOfEnum(node))
      {
        return this.Visit(node.Operand);
      }

      return base.VisitUnary(node);
    }
    
    protected override Expression VisitBinary(BinaryExpression node)
    {
      var left = node.Left;
      var right = node.Right;
      bool updated = false;
      Type? enumType = null;
    
      if (IsConversionOfEnum(left))
      {
        var unaryExpression = (UnaryExpression)left;
        left = this.Visit(unaryExpression.Operand);
        enumType = unaryExpression.Operand.Type.GetUnderlyingType();
        updated = true;
      }
    
      if (IsConversionOfEnum(right))
      {
        var unaryExpression = (UnaryExpression)right;
        right = this.Visit(unaryExpression.Operand);
        enumType = unaryExpression.Operand.Type.GetUnderlyingType();
        updated = true;
      }
    
      if (updated && right is ConstantExpression { Type.IsEnum: false } c 
                  && enumType != null && c.Value != null && Enum.IsDefined(enumType, c.Value))
      {
        right = Expression.Constant(Enum.ToObject(enumType, c.Value));
      }
    
      if (updated && left != null && right != null && left.Type != right.Type)
      {
        updated = false;
    
        if (left.Type.IsNullableValueType()
            && !right.Type.IsNullableValueType()
            && left.Type.GetUnderlyingType() == right.Type.GetUnderlyingType())
        {
          right = Expression.Convert(right, left.Type);
          updated = true;
        }
        else if (right.Type.IsNullableValueType()
                 && !left.Type.IsNullableValueType()
                 && left.Type.GetUnderlyingType() == right.Type.GetUnderlyingType()
        )
        {
          left = Expression.Convert(left, right.Type);
          updated = true;
        }
      }
    
      if (updated && left != null && right != null)
      {
        return Expression.MakeBinary(node.NodeType, left, right);
      }
      else
      {
        return base.VisitBinary(node);
      }
    }

    public override Expression? Visit(Expression? node)
    {
      if (node is NamedConstantExpression namedConstantExpression)
      {
        return node;
      }
      
      return base.Visit(node);
    }

    private bool IsConversionOfEnum(Expression node)
    {
      return node.NodeType == ExpressionType.Convert
             && node is UnaryExpression unaryExpression
             && unaryExpression.Operand.Type.GetUnderlyingType().IsEnum;
    }
  }
}