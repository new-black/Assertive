using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Assertive.Expressions;
using Xunit;

namespace Assertive.Test
{
  public class ExpressionStringBuilderTests
  {
    private readonly int _instanceField = 10;
    private static readonly int _staticField = 100;
    
    protected int MyProperty { get; set; }
    private int PrivateProperty { get; set; }
    public int PublicPropertyPrivateSetter { private get; set; }

    private class GenericClass<T>
    {
      
    }

    private class ClassWithIndexer
    {
      public int this[int a, int b] => 0;
    }
    
    [Fact]
    public void String_representation_of_expressions_are_as_expected()
    {
      var a = 1;
      var b = 2;
      var array = new int[10];
      var myClass = new MyClass();
      int? nullableA = 1;
      var list = new List<int>();
      var indexer = new ClassWithIndexer();
      
      Same(() => a + b, "a + b");
      Same(() => !(a + b == 0), "!(a + b == 0)");
      Same(() => CallFunction(a + b), "CallFunction(a + b)");
      Same(() => (long)a + b, "(long)a + (long)b");
      Same(() => (int?)a + b, "a + b");
      Same(() => nullableA == 1, "nullableA == 1");
      Same(() => nullableA == a, "nullableA == a");
      Same(() => (long)nullableA == a, "(long)nullableA == (long)a");
      Same(() => int.Parse("123"), @"int.Parse(""123"")");
      Same(() => array.Length, "array.Length");
      Same(() => array[2], "array[2]");
      Same(() => list[2], "list[2]");
      Same(() => indexer[2, 5], "indexer[2, 5]");
      Same(() => myClass[5], "myClass[5]");
      Same(() => a + b > 3 ? 10 : 12, "a + b > 3 ? 10 : 12");
      Same(() => _instanceField == 11, "_instanceField == 11");
      Same(() => _staticField == 110, "_staticField == 110");
      Same(() => MyClass.Value == 10, "MyClass.Value == 10");
      Same(() => MyProperty == 10, "MyProperty == 10");
      Same(() => PrivateProperty == 10, "PrivateProperty == 10");
      Same(() => PublicPropertyPrivateSetter == 10, "PublicPropertyPrivateSetter == 10");
      Same(() => new GenericClass<string>() == null, "new GenericClass<string>() == null");
      Same(() => new GenericClass<GenericClass<string>>() == null, "new GenericClass<GenericClass<string>>() == null");
      Same(() => new List<int> { 10, 11, 12 }.SequenceEqual(array), "new List<int>() { 10, 11, 12 }.SequenceEqual(array)");
      Same(() => new int[10] != null, "new int[10] != null");
      Same(() => new int?[10] != null, "new int?[10] != null");
      Same(() => new int[10][] != null, "new int[10][] != null");
      Same(() => new int[10][][] != null, "new int[10][][] != null");
    }

    private class MyClass
    {
      public int this[int i] => 10;
      
      public static int Value { get; set; }
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