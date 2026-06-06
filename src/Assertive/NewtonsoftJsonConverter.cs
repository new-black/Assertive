using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assertive;

internal sealed class NewtonsoftJsonConverterFactory : JsonConverterFactory
{
  public override bool CanConvert(Type typeToConvert)
  {
    return typeToConvert.Namespace == "Newtonsoft.Json.Linq";
  }

  public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
  {
    return (JsonConverter)Activator.CreateInstance(
      typeof(NewtonsoftJsonConverter<>).MakeGenericType(typeToConvert))!;
  }
}

internal sealed class NewtonsoftJsonConverter<T> : JsonConverter<T>
{
  public override bool CanConvert(Type typeToConvert)
  {
    return typeToConvert.Namespace == "Newtonsoft.Json.Linq";
  }

  public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    throw new NotSupportedException("Deserializing Newtonsoft.Json types is not supported.");
  }

  public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
  {
    if (value is null)
    {
      writer.WriteNullValue();
      return;
    }

    // JObject/JArray render valid JSON via ToString(); scalar JValues (e.g. "hello") do not,
    // so we parse and write through the writer (applying our formatting) and fall back to a
    // string value when the content isn't standalone JSON.
    var json = value.ToString()!;

    try
    {
      using var document = JsonDocument.Parse(json);
      document.RootElement.WriteTo(writer);
    }
    catch (JsonException)
    {
      writer.WriteStringValue(json);
    }
  }
}
