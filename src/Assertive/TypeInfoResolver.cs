using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
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

  public class Errors
  {
    public Stack<Dictionary<string, object>> Exceptions { get; } = new();
    public Dictionary<string, object> Current => Exceptions.Peek();
  }

  private class DefaultValueProvider<T>
  {
    public static T? Value => default;
  }

  private static readonly AsyncLocal<Errors?> _error = new(); 
  
  public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
  {
    var typeInfo = _defaultResolver.GetTypeInfo(type, options);
    if (typeInfo != null)
    {
      if (typeInfo.Kind == JsonTypeInfoKind.Object)
      {
        var errors = typeInfo.CreateJsonPropertyInfo(typeof(Dictionary<string, object>), "$Exceptions");
        errors.IsExtensionData = true;
        errors.ShouldSerialize = (_, value) => _error.Value?.Current?.Count > 0;
        errors.Get = (_) => _error.Value?.Current;
        typeInfo.Properties.Add(errors);
        typeInfo.OnSerializing = (obj) =>
        {
          _error.Value ??= new Errors();
          _error.Value.Exceptions.Push(new Dictionary<string, object>());
        };
        typeInfo.OnSerialized = (obj) => _error.Value?.Exceptions.Pop();
      }

      var properties = typeInfo.Properties.ToList();
      
      typeInfo.Properties.Clear();
      
      foreach (var property in properties)
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
              _error.Value!.Current[property.Name + "#Exception"] = _configuration.ExceptionRenderer(property, ex);
              if (property.PropertyType.IsValueType)
              {
                return typeof(DefaultValueProvider<>).MakeGenericType(property.PropertyType).InvokeMember("Value", BindingFlags.Public | BindingFlags.Static | BindingFlags.GetProperty, null, null, null);
              }

              return null;
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