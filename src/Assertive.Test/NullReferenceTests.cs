using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Sdk;
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

      ShouldFail(() => foo.Bar.StringProperty.Length == 1,
        "NullReferenceException caused by accessing Length on foo.Bar.StringProperty which was null.");
    }

    [Fact]
    public void CauseOfNullReference_on_method_is_found_three_levels()
    {
      Foo foo = new Foo();
      foo.Bar = new Bar();

      ShouldFail(() => foo.Bar.StringProperty.Replace("a", "b").Length == 1,
        "NullReferenceException caused by calling Replace on foo.Bar.StringProperty which was null.");
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

      ShouldFail(() => list.FirstOrDefault().Bar.StringField.Length == 1,
        "NullReferenceException caused by accessing Bar on list.FirstOrDefault() which was null.");
    }

    public class Survey
    {
      public class SurveyCategory
      {
        public int ID { get; set; }
      }
      public string Name { get; set; } = "foo";
      public SurveyCategory Category { get; set; }
    }

    public class AvailableSurveys
    {
      public List<Survey> Surveys { get; set; }
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_extension_method_single_item()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = null }
      };

      ShouldFail(() => surveys.Any(s => s.Category.ID == 1),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [0] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_extension_method_null_first()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = null },
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } },
        new Survey { Category = new Survey.SurveyCategory { ID = 2 } }
      };

      ShouldFail(() => surveys.Any(s => s.Category.ID == 99),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [0] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_extension_method_null_later()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } },
        new Survey { Category = new Survey.SurveyCategory { ID = 2 } },
        new Survey { Category = null }
      };

      ShouldFail(() => surveys.Any(s => s.Category.ID == 99),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [2] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_instance_method_single_item()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = null }
      };

      ShouldFail(() => surveys.Exists(s => s.Category.ID == 1),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [0] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_instance_method_null_first()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = null },
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } },
        new Survey { Category = new Survey.SurveyCategory { ID = 2 } }
      };

      ShouldFail(() => surveys.Exists(s => s.Category.ID == 99),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [0] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_instance_method_null_later()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } },
        new Survey { Category = new Survey.SurveyCategory { ID = 2 } },
        new Survey { Category = null }
      };

      ShouldFail(() => surveys.Exists(s => s.Category.ID == 99),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [2] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void CauseOfNullReference_inside_lambda_with_index_parameter()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } },
        new Survey { Category = null },
        new Survey { Category = new Survey.SurveyCategory { ID = 3 } }
      };

      // Using Select overload with (item, index) to force evaluation of all items
      ShouldFail(() => surveys.Select((s, i) => s.Category.ID + i).Sum() > 0,
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [1] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void Lambda_with_three_parameters_still_works_when_third_param_not_used_in_failing_expression()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = null },  // Null first so it gets evaluated
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } }
      };

      // The third parameter (extra) is not used in the expression that fails (s.Category.ID),
      // so Assertive can still analyze and provide a helpful message
      ShouldFail(() => surveys.CustomAnyWithThreeParams((s, i, extra) => s.Category.ID > 0),
        """
        NullReferenceException caused by accessing ID on s.Category which was null.

        On item [0] of surveys:
        { Name = "foo" }
        """);
    }

    [Fact]
    public void Lambda_with_three_parameters_falls_back_to_generic_message_when_third_param_causes_exception()
    {
      var surveys = new List<Survey>
      {
        new Survey { Category = new Survey.SurveyCategory { ID = 1 } }
      };

      // The third parameter (extra) is null and accessing .Length throws NullReferenceException.
      // Since Assertive can't bind the third parameter, it can't find the cause and falls back
      // to a generic exception message. This test verifies Assertive doesn't crash internally.
      var exception = Xunit.Assert.Throws<XunitException>(() =>
        Assert.That(() => surveys.CustomAnyWithNullThirdParam((s, i, extra) => extra!.Length > 0)));

      // Verify it's the generic fallback message (no specific cause identified)
      Xunit.Assert.Contains("Assertion threw System.NullReferenceException", exception.Message);
      Xunit.Assert.Contains("Object reference not set to an instance of an object", exception.Message);
    }
  }

  internal static class ThreeParamLambdaExtensions
  {
    /// <summary>
    /// Test extension method with a 3-parameter lambda to verify Assertive handles it gracefully.
    /// </summary>
    public static bool CustomAnyWithThreeParams<T>(this IEnumerable<T> source, Func<T, int, string, bool> predicate)
    {
      var index = 0;
      foreach (var item in source)
      {
        if (predicate(item, index, "extra"))
        {
          return true;
        }
        index++;
      }
      return false;
    }

    /// <summary>
    /// Test extension method with a 3-parameter lambda where the third param is null.
    /// </summary>
    public static bool CustomAnyWithNullThirdParam<T>(this IEnumerable<T> source, Func<T, int, string?, bool> predicate)
    {
      var index = 0;
      foreach (var item in source)
      {
        if (predicate(item, index, null))  // Third param is null
        {
          return true;
        }
        index++;
      }
      return false;
    }
  }
}