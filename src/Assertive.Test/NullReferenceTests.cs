using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class NullReferenceTests : AssertionTestBase
  {
    private class Foo
    {
      public string StringProperty { get; set; }
      public string StringField = null;

      public Bar Bar { get; set; }

      public string ReturnsNull()
      {
        return null;
      }

      public string ReturnsNotNull()
      {
        return "something";
      }
    }

    private class Bar
    {
      public string StringProperty { get; set; }
      public string StringField = null;

      public string Throws => throw new NullReferenceException();

      public string MethodThrows()
      {
        throw new NullReferenceException();
      }
      
      public string ReturnsNotNull()
      {
        return "something";
      }
    }

    [Fact]
    public void CauseOfNullReference_on_property_is_found_single_level()
    {
      Foo foo = null;
      
      ShouldFail(() => foo.StringProperty.Length == 1, "NullReferenceException caused by accessing StringProperty on foo which was null.");
      ShouldFail(() => foo.StringProperty == "A", "NullReferenceException caused by accessing StringProperty on foo which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_property_is_found_on_array_index()
    {
      int[] array = null;
      
      ShouldFail(() => array[0] == 1, "NullReferenceException caused by accessing array index 0 on array which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_property_is_found_on_array_length()
    {
      int[] array = null;
      
      ShouldFail(() => array.Length == 1, "NullReferenceException caused by accessing array length on array which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_method_is_found_single_level()
    {
      Foo foo = null;
      
      ShouldFail(() => foo.ReturnsNotNull().Length == 1, "NullReferenceException caused by calling ReturnsNotNull on foo which was null.");
      ShouldFail(() => foo.ReturnsNotNull() == "A", "NullReferenceException caused by calling ReturnsNotNull on foo which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_field_is_found_single_level()
    {
      Foo foo = null;
      
      ShouldFail(() => foo.StringField.Length == 1, "NullReferenceException caused by accessing StringField on foo which was null.");
      ShouldFail(() => foo.StringField == "A", "NullReferenceException caused by accessing StringField on foo which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_method_is_found_two_levels()
    {
      Foo foo = new Foo();
      
      ShouldFail(() => foo.Bar.ReturnsNotNull().Length == 1, "NullReferenceException caused by calling ReturnsNotNull on foo.Bar which was null.");
      ShouldFail(() => foo.Bar.ReturnsNotNull() == "A", "NullReferenceException caused by calling ReturnsNotNull on foo.Bar which was null.");
      ShouldFail(() => foo.ReturnsNull().Length == 1, "NullReferenceException caused by accessing Length on foo.ReturnsNull() which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_property_is_found_two_levels()
    {
      Foo foo = new Foo();
      
      ShouldFail(() => foo.Bar.StringProperty.Length == 1, "NullReferenceException caused by accessing StringProperty on foo.Bar which was null.");
      ShouldFail(() => foo.Bar.StringProperty == "A", "NullReferenceException caused by accessing StringProperty on foo.Bar which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_property_is_found_three_levels()
    {
      Foo foo = new Foo();
      foo.Bar = new Bar();
      
      ShouldFail(() => foo.Bar.StringProperty.Length == 1, "NullReferenceException caused by accessing Length on foo.Bar.StringProperty which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_method_is_found_three_levels()
    {
      Foo foo = new Foo();
      foo.Bar = new Bar();
      
      ShouldFail(() => foo.Bar.StringProperty.Replace("a", "b").Length == 1, "NullReferenceException caused by calling Replace on foo.Bar.StringProperty which was null.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_property_is_found_when_thrown_internally()
    {
      Foo foo = new Foo();
      foo.Bar = new Bar();
      
      ShouldFail(() => foo.Bar.Throws.Length == 10, "NullReferenceException was thrown inside Throws on foo.Bar.Throws.");
    }
    
    [Fact]
    public void CauseOfNullReference_on_method_is_found_when_thrown_internally()
    {
      Foo foo = new Foo();
      foo.Bar = new Bar();
      
      ShouldFail(() => foo.Bar.MethodThrows().Length == 10, "NullReferenceException was thrown inside MethodThrows on foo.Bar.MethodThrows().");
    }

    [Fact]
    public void First_or_default_on_empty_collection()
    {
      var list = new List<Foo>();

      ShouldFail(() => list.FirstOrDefault().Bar.StringField.Length == 1, "NullReferenceException caused by accessing Bar on list.FirstOrDefault() which was null.");
    }
  }
}