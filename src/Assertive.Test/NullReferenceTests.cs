using System;
using Xunit;

namespace Assertive.Test
{
  public class NullReferenceTests : AssertionTestBase
  {
    private class Foo
    {
      public string StringProperty { get; set; }
      public string StringField;

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
      public string StringField;

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
  }
}