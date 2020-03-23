using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class SerializerTests
  {
    private class ThrowsClass
    {
      public string Throws => throw new Exception();
    }
    
    [Fact]
    public void Exception_inside_serialization_doesnt_throw()
    {
      var obj= new ThrowsClass();

      var result = Serializer.Serialize(obj, 0, null);
      
      Assert(() => result == "<exception serializing>");
    }

    private class ClassWithEnumerable
    {
      public IEnumerable<int> Items => Enumerable.Range(0, 5);
    }

    
    private class ClassWithComplexEnumerable
    {
      public class Item
      {
        public string Value { get; set; }
      }
      
      public IEnumerable<Item> Items => Enumerable.Range(0, 5).Select(x => new Item { Value = x.ToString() });
    }

    [Fact]
    public void Primitives_are_serialized_directly()
    {
      Assert(() => Serializer.Serialize(1, 0, null) == "1");
      Assert(() => Serializer.Serialize(true, 0, null) == "True");
      Assert(() => Serializer.Serialize("my value", 0, null) == "my value");
    }

    class RecursiveReference
    {
      public RecursiveReference Self { get; set; }
      public List<RecursiveReference> Selfs { get; set; }
    }

    [Fact]
    public void Infinite_recursion_is_handled_safely()
    {
      var obj = new RecursiveReference();

      obj.Self = obj;
      obj.Selfs = new List<RecursiveReference>()
      {
        obj
      };

      var result = Serializer.Serialize(obj, 0, null);

      Assert(() => result == "{ Self = <infinite recursion>, Selfs = [ <infinite recursion> ] }");
    }
  
    [Fact]
    public void Enumerable_property_of_complex_type_is_serialized()
    {
      var result = Serializer.Serialize(new ClassWithComplexEnumerable(), 0, null);
      
      Assert(() => result == "{ Items = [ { Value = 0 }, { Value = 1 }, { Value = 2 }, { Value = 3 }, { Value = 4 } ] }");
    }
    
    [Fact]
    public void Enumerable_property_is_serialized()
    {
      var result = Serializer.Serialize(new ClassWithEnumerable(), 0, null);
      
      Assert(() => result == "{ Items = [ 0, 1, 2, 3, 4 ] }");
    }
    
    [Fact]
    public void List_is_serialized()
    {
      var result = Serializer.Serialize(new List<int>() { 1, 2, 3, 4 }, 0, null);
      
      Assert(() => result == "[ 1, 2, 3, 4 ]");
    }
  }
}