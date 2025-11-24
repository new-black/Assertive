using System;
using System.Linq.Expressions;

namespace Assertive.Expressions
{
  /// <summary>
  /// Wrapper used to hint the expression formatter to render a method call as a static call
  /// (method(args) on source) instead of the default instance/extension method syntax.
  /// </summary>
  internal sealed class StaticRenderMethodCallExpression : Expression
  {
    public MethodCallExpression Original { get; }

    private StaticRenderMethodCallExpression(MethodCallExpression original)
    {
      Original = original ?? throw new ArgumentNullException(nameof(original));
    }

    public static StaticRenderMethodCallExpression Wrap(MethodCallExpression original) => new(original);

    public override ExpressionType NodeType => ExpressionType.Extension;
    public override Type Type => Original.Type;
    public override bool CanReduce => false;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
      var visited = (MethodCallExpression)visitor.Visit(Original)!;
      return visited == Original ? this : new StaticRenderMethodCallExpression(visited);
    }
  }
}
