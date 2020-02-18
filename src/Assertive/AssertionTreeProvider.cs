using System;
using System.Linq.Expressions;

namespace Assertive
{
  internal class AssertionTreeProvider : ExpressionVisitor
  {
    private readonly Expression<Func<bool>> _assertion;

    public AssertionTreeProvider(Expression<Func<bool>> assertion)
    {
      _assertion = assertion;
    }

    private AssertionNode? _currentNode;

    internal AssertionNode GetTree()
    {
      this.Visit(_assertion.Body);

      return _currentNode ??= GetAssertionNode(_assertion.Body);
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
      return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
      if (node.Type == typeof(bool))
      {
        GetAssertionNode(node);
      }

      return node;
    }
    
    private AssertionNode GetAssertionNode(Expression node)
    {
      AssertionNode AssertionNodeImpl()
      {
        if (node is BinaryExpression binaryExpression)
        {
          switch (binaryExpression.NodeType)
          {
            case ExpressionType.And: return ProcessBinary(binaryExpression, AssertionNodeType.And);
            case ExpressionType.AndAlso: return ProcessBinary(binaryExpression, AssertionNodeType.AndAlso);
            case ExpressionType.Or: return ProcessBinary(binaryExpression, AssertionNodeType.Or);
            case ExpressionType.OrElse: return ProcessBinary(binaryExpression, AssertionNodeType.OrElse);
            default:
              return new AssertionNode(node, AssertionNodeType.Leaf);
          }
        }

        return new AssertionNode(node, AssertionNodeType.Leaf);
      }

      _currentNode = AssertionNodeImpl();

      return _currentNode;
    }

    private AssertionNode ProcessBinary(BinaryExpression binaryExpression, AssertionNodeType type)
    {
      var left = GetAssertionNode(binaryExpression.Left);

      var right = GetAssertionNode(binaryExpression.Right);

      return new AssertionNode(binaryExpression, type)
      {
        Left = left,
        Right = right
      };
    }
  }
  
  internal class AssertionNode
  {
    public AssertionNode(Expression expression, AssertionNodeType type)
    {
      Expression = expression;
      Type = type;
    }

    public Expression Expression { get; }
    public AssertionNodeType Type { get; }

    public AssertionNode? Left { get; set; }
    public AssertionNode? Right { get; set; }
  }

  internal enum AssertionNodeType
  {
    Leaf,
    And,
    AndAlso,
    Or,
    OrElse
  }

}