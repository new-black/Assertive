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
      var obj = new ThrowsClass();

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

    private class DateTimeClass
    {
      public DateTime Since { get; set; }
      public DateTime Other { get; set; }
    }

    [Fact]
    public void DateTime_works()
    {
      var obj = new DateTimeClass();

      obj.Since = new DateTime(2020, 1, 1);

      var result = Serializer.Serialize(obj);

      Assert(() => result == @"{ Since = 2020-01-01T00:00:00.0000000, Other = 0001-01-01T00:00:00.0000000 }");
    }

    [Fact]
    public void Enumerable_property_of_complex_type_is_serialized()
    {
      var result = Serializer.Serialize(new ClassWithComplexEnumerable());

      Assert(() =>
        result ==
        @"{ Items = [ { Value = ""0"" }, { Value = ""1"" }, { Value = ""2"" }, { Value = ""3"" }, { Value = ""4"" } ] }");
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

    class ClassWithDictionary
    {
      public IDictionary<string, int> InterfaceDict { get; set; }
      public Dictionary<int, string[]> DictWithArray { get; set; }
    }

    [Fact]
    public void Dictionary_properties_are_serialized_correctly()
    {
      var c = new ClassWithDictionary()
      {
        InterfaceDict = new Dictionary<string, int>()
        {
          ["test"] = 123,
          ["abc"] = 456
        },
        DictWithArray = new Dictionary<int, string[]>()
        {
          [1234] = new[] { "abc" },
          [45678] = new[] { "def" },
        }
      };
      
      var result = Serializer.Serialize(c);
      
      Assert(() => result == "{ InterfaceDict = { [\"test\"] = 123, [\"abc\"] = 456 }, DictWithArray = { [1234] = [ \"abc\" ], [45678] = [ \"def\" ] } }");
    }

    [Fact]
    public void Dictionary_is_serialized_correctly()
    {
      var dict = new Dictionary<string, string[]>()
      {
        ["abc"] = new[] { "1234" },
        ["def"] = new[] { "4567" }
      };

      var result = Serializer.Serialize(dict);

      Assert(() => result == "{ [\"abc\"] = [ \"1234\" ], [\"def\"] = [ \"4567\" ] }");
    }

    [Fact]
    public void Doubles_are_serialized_correctly()
    {
      Assert(() => Serializer.Serialize(52.438) == "52.438");
    }
  }
}