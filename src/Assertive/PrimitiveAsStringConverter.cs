using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Assertive;

internal class PrimitiveAsStringConverter<T> : JsonConverter<T>
{
  private readonly Func<JsonPropertyInfo, object?, object?>? _valueRenderer;
  private readonly JsonPropertyInfo _propertyInfo;
  private readonly JsonConverter<T> _defaultConverter;

  public PrimitiveAsStringConverter(Func<JsonPropertyInfo, object?, object?>? valueRenderer, JsonPropertyInfo propertyInfo,
    JsonConverter<T> defaultConverter)
  {
    _valueRenderer = valueRenderer;
    _propertyInfo = propertyInfo;
    _defaultConverter = defaultConverter;
  }

  public override bool CanConvert(Type typeToConvert)
  {
    return true;
  }

  public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    throw new NotImplementedException();
  }

  public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
  {
    var renderedValue = _valueRenderer?.Invoke(_propertyInfo, value);

    if (renderedValue != null)
    {
      writer.WriteStringValue(renderedValue.ToString());
    }
    else
    {
      _defaultConverter.Write(writer, value, options);
    }
  }
}