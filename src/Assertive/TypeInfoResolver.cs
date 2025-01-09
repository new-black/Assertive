using System;
using System.Linq;
using System.Text.Json;
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

    static bool IsDateType(Type t)
    {
      return t.IsType<DateTime>() || t.IsType<DateTimeOffset>() || t.IsType<TimeSpan>() || t.IsType<TimeOnly>() || t.IsType<DateOnly>();
    }
    
    if (typeInfo is { Kind: JsonTypeInfoKind.Object })
    {
      var properties = typeInfo.Properties.ToList();

      typeInfo.Properties.Clear();

      foreach (var existingProperty in properties)
      {
        var newProperty = typeInfo.CreateJsonPropertyInfo(typeof(object), existingProperty.Name);
        typeInfo.Properties.Add(newProperty);
          
        if (_configuration.Normalization.NormalizeGuid && existingProperty.PropertyType.GetUnderlyingType().IsType<Guid>())
        {
          newProperty.Get = CreateGetter(existingProperty, (_, _, _) => "{Guid}");
        }
        else if (_configuration.Normalization.NormalizeDateTime && (IsDateType(existingProperty.PropertyType.GetUnderlyingType())))
        {
          var typeName = existingProperty.PropertyType.GetUnderlyingType().Name;
          newProperty.Get = CreateGetter(existingProperty, (_, _, _) => $"{{{typeName}}}");
        }
        else
        {
          newProperty.Get = CreateGetter(existingProperty, _configuration.Normalization.ValueRenderer);
        }

        if (_configuration.ShouldIgnore != null)
        {
          if (_configuration.ExcludeNullValues)
          {
            newProperty.ShouldSerialize = (obj, value) => value != null && !_configuration.ShouldIgnore(existingProperty, obj, value);
          }
          else
          {
            newProperty.ShouldSerialize = (obj, value) => !_configuration.ShouldIgnore(existingProperty, obj, value);
          }
        }
        else if (_configuration.ExcludeNullValues)
        {
          newProperty.ShouldSerialize = (_, value) => value != null;
        }
      }
    }

    return typeInfo;
  }

  private Func<object, object?> CreateGetter(JsonPropertyInfo existingProperty, Configuration.ValueRenderer? valueRenderer)
  {
    var originalGetter = existingProperty.Get;
    
    return obj =>
    {
      try
      {
        var value = originalGetter!(obj);

        return valueRenderer != null ? valueRenderer(existingProperty, obj, value) : value;
      }
      catch (Exception ex)
      {
        return _configuration.ExceptionRenderer(existingProperty, obj, ex);
      }
    };
  }
}