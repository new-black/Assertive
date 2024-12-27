using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Assertive.Config;
using Assertive.Helpers;

namespace Assertive;

internal class TypeInfoResolver : IJsonTypeInfoResolver
{
  private readonly Configuration.CompareSnapshotsConfiguration _configuration;
  private readonly IJsonTypeInfoResolver _defaultResolver;

  public TypeInfoResolver(Configuration.CompareSnapshotsConfiguration configuration)
  {
    _configuration = configuration;
    _defaultResolver = new DefaultJsonTypeInfoResolver();
  }

  public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
  {
    var typeInfo = _defaultResolver.GetTypeInfo(type, options);
    if (typeInfo != null)
    {
      foreach (var property in typeInfo.Properties)
      {
        var originalGetter = property.Get;
        var propertyTypeInfo = _defaultResolver.GetTypeInfo(property.PropertyType, options);

        if (_configuration.Normalization.NormalizeGuid && property.PropertyType.GetUnderlyingType().IsType<Guid>())
        {
          //property.Get => (obj) =>
        }
        else if (_configuration.Normalization.NormalizeDateTime && (property.PropertyType.GetUnderlyingType().IsType<DateTime>() ||
                                                                    property.PropertyType.GetUnderlyingType()
                                                                      .IsType<DateTimeOffset>())) { }
        else
        {
          if (propertyTypeInfo is { Kind: JsonTypeInfoKind.None })
          {
            var converter = (JsonConverter)Activator.CreateInstance(typeof(PrimitiveAsStringConverter<>).MakeGenericType(property.PropertyType),
              _configuration.ValueRenderer, property,
              propertyTypeInfo.Converter)!;
            property.CustomConverter = converter;
          }

          property.Get = (obj) =>
          {
            try
            {
              return originalGetter!(obj);
            }
            catch (Exception ex)
            {
              return _configuration.ExceptionRenderer(property, ex);
            }
          };
        }

        if (_configuration.ShouldIgnore != null)
        {
          if (_configuration.ExcludeNullValues)
          {
            property.ShouldSerialize = (obj, value) => value != null && !_configuration.ShouldIgnore(property, obj, value);
          }
          else
          {
            property.ShouldSerialize = (obj, value) => !_configuration.ShouldIgnore(property, obj, value);
          }
        }
        else if(_configuration.ExcludeNullValues)
        {
          property.ShouldSerialize = (_, value) => value != null;
        }
      }
    }

    return typeInfo;
  }
}