using System;
using System.Linq.Expressions;

namespace Assertive.Expressions
{
  internal class NamedConstantExpression : Expression
  {
    public string Name { get; }
    public object Value { get; }

    public NamedConstantExpression(string name, object value)
    {
      Name = name;
      Value = value;
    }

    public override ExpressionType NodeType => (ExpressionType)(CustomExpressionTypes.NamedConstant);

    public override Expression Reduce()
    {
      return Constant(Value);
    }

    public override bool CanReduce => true;

    public override Type Type
    {
      get
      {
        if (Value == null)
        {
          return typeof(object);
        }

        return Value.GetType();
      }
    }
  }

}