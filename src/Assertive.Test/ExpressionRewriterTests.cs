using System;
using System.Linq.Expressions;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class ExpressionRewriterTests 
  {
    [Fact]
    public void Enum_variable_comparison_is_rewritten()
    {
      var a = MyEnum.A;
      var b = MyEnum.B;
      
      ShouldEqual(() => a == b, "a == b");
    }
    
    [Fact]
    public void Nullable_Enum_variable_comparison_is_rewritten()
    {
      MyEnum? a = MyEnum.A;
      MyEnum? b = MyEnum.B;
      
      ShouldEqual(() => a == b, "a == b");
    }
    
    [Fact]
    public void Enum_literal_comparison_is_rewritten()
    {
      var a = MyEnum.A;
      
      ShouldEqual(() => a == MyEnum.B, "a == MyEnum.B");
    }

    [Fact]
    public void Nullable_Enum_literal_comparison_is_rewritten()
    {
      MyEnum? a = MyEnum.A;
      
      ShouldEqual(() => a == MyEnum.B, "a == MyEnum.B");
    }
    
    private enum MyEnum
    {
      A = 0,
      B = 1
    }

    private void ShouldEqual(Expression<Func<bool>> assertion, string toString)
    {
      var rewriter = new ExpressionRewriter();

      var result = (LambdaExpression)rewriter.Visit(assertion);

      var str = ExpressionStringBuilder.ExpressionToString(result.Body);
      
      Assert(() => str.StartsWith(toString));
    }
  }
}