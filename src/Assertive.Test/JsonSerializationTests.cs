using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Assertive.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Assertive.Test
{
  /// <summary>
  /// Tests that JSON types from both Newtonsoft.Json and System.Text.Json are serialized
  /// as proper JSON rather than being treated as enumerables or objects.
  /// </summary>
  public class JsonSerializationTests : AssertionTestBase
  {
    private static string StripAnsi(string input)
    {
      return Regex.Replace(input, @"\u001b\[[0-9;]*[A-Za-z]", "");
    }

    #region Newtonsoft.Json - Assertion failure output tests

    [Fact]
    public void Newtonsoft_JObject_InFailedAssertion_ShowsJson()
    {
      var obj = new JObject
      {
        ["name"] = "John",
        ["age"] = 30
      };

      ShouldFail(
        () => obj == null,
        "obj should be null",
        @"""name"": ""John"""
      );
    }

    [Fact]
    public void Newtonsoft_JArray_InFailedAssertion_ShowsJson()
    {
      var arr = new JArray { 1, 2, 3 };

      ShouldFail(
        () => arr == null,
        "arr should be null",
        """
        [
          1,
          2,
          3
        ]
        """
      );
    }

    [Fact]
    public void Newtonsoft_NestedJObject_InFailedAssertion_ShowsNestedJson()
    {
      var obj = new JObject
      {
        ["user"] = new JObject
        {
          ["name"] = "Alice"
        }
      };

      ShouldFail(
        () => obj == null,
        "obj should be null",
        @"""name"": ""Alice"""
      );
    }

    #endregion

    #region Newtonsoft.Json - Serializer unit tests

    [Fact]
    public void Newtonsoft_JObject_SerializesAsJson()
    {
      var obj = new JObject
      {
        ["name"] = "John",
        ["age"] = 30
      };

      var result = StripAnsi(Serializer.Serialize(obj).ToString());
      var expected = """
        {
          "name": "John",
          "age": 30
        }
        """;

      Assert.That(() => result == expected);
    }

    [Fact]
    public void Newtonsoft_JArray_SerializesAsJson()
    {
      var arr = new JArray { 1, 2, 3 };

      var result = StripAnsi(Serializer.Serialize(arr).ToString());
      var expected = """
        [
          1,
          2,
          3
        ]
        """;

      Assert.That(() => result == expected);
    }

    [Fact]
    public void Newtonsoft_JValue_String_SerializesAsValue()
    {
      var val = new JValue("hello");

      var result = StripAnsi(Serializer.Serialize(val).ToString());

      Assert.That(() => result == "hello");
    }

    [Fact]
    public void Newtonsoft_JValue_Number_SerializesAsValue()
    {
      var val = new JValue(42);

      var result = StripAnsi(Serializer.Serialize(val).ToString());

      Assert.That(() => result == "42");
    }

    [Fact]
    public void Newtonsoft_NestedJObject_SerializesAsJson()
    {
      var obj = new JObject
      {
        ["person"] = new JObject
        {
          ["name"] = "John",
          ["addresses"] = new JArray
          {
            new JObject { ["city"] = "New York" },
            new JObject { ["city"] = "London" }
          }
        }
      };

      var result = StripAnsi(Serializer.Serialize(obj).ToString());
      var expected = """
        {
          "person": {
            "name": "John",
            "addresses": [
              {
                "city": "New York"
              },
              {
                "city": "London"
              }
            ]
          }
        }
        """;

      Assert.That(() => result == expected);
    }

    #endregion

    #region System.Text.Json.Nodes - Assertion failure output tests

    [Fact]
    public void SystemTextJson_JsonObject_InFailedAssertion_ShowsJson()
    {
      var obj = new JsonObject
      {
        ["name"] = "John",
        ["age"] = 30
      };

      ShouldFail(
        () => obj == null,
        "obj should be null",
        @"""name"": ""John"""
      );
    }

    [Fact]
    public void SystemTextJson_JsonArray_InFailedAssertion_ShowsJson()
    {
      var arr = new JsonArray { 1, 2, 3 };

      ShouldFail(
        () => arr == null,
        "arr should be null",
        "1"
      );
    }

    [Fact]
    public void SystemTextJson_NestedJsonObject_InFailedAssertion_ShowsNestedJson()
    {
      var obj = new JsonObject
      {
        ["user"] = new JsonObject
        {
          ["name"] = "Alice"
        }
      };

      ShouldFail(
        () => obj == null,
        "obj should be null",
        @"""name"": ""Alice"""
      );
    }

    #endregion

    #region System.Text.Json.Nodes - Serializer unit tests

    [Fact]
    public void SystemTextJson_JsonObject_SerializesAsJson()
    {
      var obj = new JsonObject
      {
        ["name"] = "John",
        ["age"] = 30
      };

      var result = StripAnsi(Serializer.Serialize(obj).ToString());
      var expected = """
        {
          "name": "John",
          "age": 30
        }
        """;

      Assert.That(() => result == expected);
    }

    [Fact]
    public void SystemTextJson_JsonArray_SerializesAsJson()
    {
      var arr = new JsonArray { 1, 2, 3 };

      var result = StripAnsi(Serializer.Serialize(arr).ToString());
      var expected = """
        [
          1,
          2,
          3
        ]
        """;

      Assert.That(() => result == expected);
    }

    [Fact]
    public void SystemTextJson_JsonValue_String_SerializesAsValue()
    {
      var val = JsonValue.Create("hello");

      var result = StripAnsi(Serializer.Serialize(val).ToString());

      Assert.That(() => result == "hello");
    }

    [Fact]
    public void SystemTextJson_JsonValue_Number_SerializesAsValue()
    {
      var val = JsonValue.Create(42);

      var result = StripAnsi(Serializer.Serialize(val).ToString());

      Assert.That(() => result == "42");
    }

    [Fact]
    public void SystemTextJson_NestedJsonObject_SerializesAsJson()
    {
      var obj = new JsonObject
      {
        ["person"] = new JsonObject
        {
          ["name"] = "John",
          ["addresses"] = new JsonArray
          {
            new JsonObject { ["city"] = "New York" },
            new JsonObject { ["city"] = "London" }
          }
        }
      };

      var result = StripAnsi(Serializer.Serialize(obj).ToString());
      var expected = """
        {
          "person": {
            "name": "John",
            "addresses": [
              {
                "city": "New York"
              },
              {
                "city": "London"
              }
            ]
          }
        }
        """;

      Assert.That(() => result == expected);
    }

    #endregion

    #region System.Text.Json - JsonElement and JsonDocument

    [Fact]
    public void SystemTextJson_JsonElement_Object_SerializesAsJson()
    {
      using var doc = JsonDocument.Parse("""{"name": "John", "age": 30}""");
      var element = doc.RootElement;

      var result = StripAnsi(Serializer.Serialize(element).ToString());

      Assert.That(() => result.Contains(@"""name"""));
      Assert.That(() => result.Contains(@"""John"""));
      Assert.That(() => result.Contains(@"""age"""));
      Assert.That(() => result.Contains("30"));
    }

    [Fact]
    public void SystemTextJson_JsonElement_Array_SerializesAsJson()
    {
      using var doc = JsonDocument.Parse("[1, 2, 3]");
      var element = doc.RootElement;

      var result = StripAnsi(Serializer.Serialize(element).ToString());

      Assert.That(() => result.Contains("1"));
      Assert.That(() => result.Contains("2"));
      Assert.That(() => result.Contains("3"));
    }

    [Fact]
    public void SystemTextJson_JsonElement_String_SerializesAsValue()
    {
      using var doc = JsonDocument.Parse(@"""hello""");
      var element = doc.RootElement;

      var result = StripAnsi(Serializer.Serialize(element).ToString());

      Assert.That(() => result == "hello");
    }

    [Fact]
    public void SystemTextJson_JsonElement_Number_SerializesAsValue()
    {
      using var doc = JsonDocument.Parse("42");
      var element = doc.RootElement;

      var result = StripAnsi(Serializer.Serialize(element).ToString());

      Assert.That(() => result == "42");
    }

    [Fact]
    public void SystemTextJson_JsonDocument_SerializesAsJson()
    {
      using var doc = JsonDocument.Parse("""{"status": "ok"}""");

      var result = StripAnsi(Serializer.Serialize(doc).ToString());

      Assert.That(() => result.Contains(@"""status"""));
      Assert.That(() => result.Contains(@"""ok"""));
    }

    #endregion

    #region Mixed JSON types in objects

    [Fact]
    public void MixedJsonTypes_InAnonymousObject_SerializeCorrectly()
    {
      var x = new
      {
        A = 1,
        B = new JObject
        {
          ["name"] = "John",
          ["age"] = 30
        },
        C = new JsonObject
        {
          ["name"] = "John",
          ["age"] = 30
        }
      };

      try
      {
        Assert.That(() => x.A == 2);
      }
      catch (System.Exception ex)
      {
        Assert.That(() => StripAnsi(ex.Message).Contains("""
                                                         x: { A = 1, B = {
                                                           "name": "John",
                                                           "age": 30
                                                         }, C = {
                                                           "name": "John",
                                                           "age": 30
                                                         } }
                                                         """));
      }
    }

    #endregion
  }
}
