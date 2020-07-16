using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Helpers;
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

      var result = Serializer.Serialize(obj);
      
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
      Assert(() => Serializer.Serialize(1) == "1");
      Assert(() => Serializer.Serialize(true) == "True");
      Assert(() => Serializer.Serialize("my value") == @"""my value""");
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

      var result = Serializer.Serialize(obj);

      Assert(() => result == "{ Self = <infinite recursion>, Selfs = [ <infinite recursion> ] }");
    }
  
    [Fact]
    public void Enumerable_property_of_complex_type_is_serialized()
    {
      var result = Serializer.Serialize(new ClassWithComplexEnumerable());
      
      Assert(() => result == @"{ Items = [ { Value = ""0"" }, { Value = ""1"" }, { Value = ""2"" }, { Value = ""3"" }, { Value = ""4"" } ] }");
    }
    
    [Fact]
    public void Enumerable_property_is_serialized()
    {
      var result = Serializer.Serialize(new ClassWithEnumerable());
      
      Assert(() => result == "{ Items = [ 0, 1, 2, 3, 4 ] }");
    }
    
    [Fact]
    public void List_is_serialized()
    {
      var result = Serializer.Serialize(new List<int>() { 1, 2, 3, 4 });
      
      Assert(() => result == "[ 1, 2, 3, 4 ]");
    }

    [Fact]
    public void Only_non_null_properties_are_serialized()
    {
      var c = new ComplexClass()
      {
        G = "123",
        D = "ABC"
      };
      
      var result = Serializer.Serialize(c);
      
      Assert(() => result == @"{ D = ""ABC"", G = ""123"" }");
    }
    
    [Fact]
    public void Long_messages_become_multiline()
    {
      var c = new ComplexClass()
      {
        A = "abcedghijklmnopqrstuvwxyz",
        B = "abcedghijklmnopqrstuvwxyz",
        C = "abcedghijklmnopqrstuvwxyz",
        D = "abcedghijklmnopqrstuvwxyz",
        E = "abcedghijklmnopqrstuvwxyz",
        F = "abcedghijklmnopqrstuvwxyz",
        G = "abcedghijklmnopqrstuvwxyz",
        H = "abcedghijklmnopqrstuvwxyz",
        J = "abcedghijklmnopqrstuvwxyz",
        K = "abcedghijklmnopqrstuvwxyz",
        L = "abcedghijklmnopqrstuvwxyz",
        M = "abcedghijklmnopqrstuvwxyz",
        N = "abcedghijklmnopqrstuvwxyz",
        O = "abcedghijklmnopqrstuvwxyz",
      };
      
      var result = Serializer.Serialize(c);
      
      Assert(() => result == @"{
 A = ""abcedghijklmnopqrstuvwxyz"",
 B = ""abcedghijklmnopqrstuvwxyz"",
 C = ""abcedghijklmnopqrstuvwxyz"",
 D = ""abcedghijklmnopqrstuvwxyz"",
 E = ""abcedghijklmnopqrstuvwxyz"",
 F = ""abcedghijklmnopqrstuvwxyz"",
 G = ""abcedghijklmnopqrstuvwxyz"",
 H = ""abcedghijklmnopqrstuvwxyz"",
 J = ""abcedghijklmnopqrstuvwxyz"",
 K = ""abcedghijklmnopqrstuvwxyz"",
 L = ""abcedghijklmnopqrstuvwxyz"",
 M = ""abcedghijklmnopqrstuvwxyz"",
 N = ""abcedghijklmnopqrstuvwxyz"",
 O = ""abcedghijklmnopqrstuvwxyz""
}");
    }

      class ComplexClass
    {
      public string A { get; set; }
      public string B { get; set; }
      public string C { get; set; }
      public string D { get; set; }
      public string E { get; set; }
      public string F { get; set; }
      public string G { get; set; }
      public string H { get; set; }
      public string I { get; set; }
      public string J { get; set; }
      public string K { get; set; }
      public string L { get; set; }
      public string M { get; set; }
      public string N { get; set; }
      public string O { get; set; }
    }
  }
}