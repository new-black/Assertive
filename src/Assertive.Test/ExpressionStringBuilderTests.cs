using System;
using System.Linq.Expressions;
using Xunit;

namespace Assertive.Test
{
  public class ExpressionStringBuilderTests
  {
    [Fact]
    public void String_representation_of_expressions_are_as_expected()
    {
      var a = 1;
      var b = 2;
      var array = new int[10];
      var myClass = new MyClass();
      
      Same(() => a + b, "a + b");
      Same(() => CallFunction(a + b), "CallFunction(a + b)");
      Same(() => (long)a + b, "(long)a + (long)b");
      Same(() => (int?)a + b, "(int?)a + (int?)b");
      Same(() => int.Parse("123"), @"int.Parse(""123"")");
      Same(() => array.Length, "array.Length");
      Same(() => array[2], "array[2]");
      Same(() => myClass[5], "myClass[5]");
      Same(() => a + b > 3 ? 10 : 12, "a + b > 3 ? 10 : 12");
    }

    private class MyClass
    {
      public int this[int i] => 10;
    }

    private static void CallFunction(int value)
    {
      
    }

    private void Same(Expression<Action> expression, string str)
    {
      Xunit.Assert.Equal(ExpressionStringBuilder.ExpressionToString(expression.Body), str);
    }
    
    private void Same(Expression<Func<object>> expression, string str)
    {
      Expression bodyExpression;

      if (expression.Body.NodeType == ExpressionType.Convert 
          && expression.Body is UnaryExpression convertExpression 
          && expression.Body.Type == typeof(object))
      {
        bodyExpression = convertExpression.Operand;
      }
      else
      {
        bodyExpression = expression.Body;
      }
      
      Xunit.Assert.Equal(str, ExpressionStringBuilder.ExpressionToString(bodyExpression));
    }
  }
}