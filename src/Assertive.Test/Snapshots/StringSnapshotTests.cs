using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class StringSnapshotTests
{
  [Fact]
  public void String_snapshot_stores_plain_text()
  {
    var text = "Hello, World!";

    Assert(text);
  }

  [Fact]
  public void String_snapshot_with_newlines()
  {
    var text = @"Line 1
Line 2
Line 3";

    Assert(text);
  }

  [Fact]
  public void String_snapshot_with_special_characters()
  {
    var text = "Special chars: @#$%^&*(){}[]<>?/\\|";

    Assert(text);
  }

  [Fact]
  public void Newtonsoft_JObject_snapshot()
  {
    var obj = new JObject
    {
      ["name"] = "John",
      ["age"] = 30
    };

    Assert(obj);
  }

  [Fact]
  public void Newtonsoft_JArray_snapshot()
  {
    var arr = new JArray { 1, 2, 3 };

    Assert(arr);
  }

  [Fact]
  public void System_Text_Json_JsonObject_snapshot()
  {
    var obj = new JsonObject
    {
      ["name"] = "John",
      ["age"] = 30
    };

    Assert(obj);
  }

  [Fact]
  public void System_Text_Json_JsonArray_snapshot()
  {
    var arr = new JsonArray { 1, 2, 3 };

    Assert(arr);
  }

  [Fact]
  public void System_Text_Json_JsonDocument_snapshot()
  {
    using var doc = JsonDocument.Parse("""{"name":"John","age":30}""");

    Assert(doc);
  }

  [Fact]
  public void System_Text_Json_JsonElement_snapshot()
  {
    using var doc = JsonDocument.Parse("""{"name":"John","age":30}""");
    var element = doc.RootElement.Clone();

    Assert(element);
  }
}
