using System.Collections;
using System.Collections.Generic;
using Assertive.Config;

namespace Assertive.Helpers;

internal static class ProjectionHelper
{
  /// <summary>
  /// Applies a snapshot projection to <paramref name="value"/>, recursing into the elements of
  /// enumerables so projections also apply inside collections. Property-level recursion happens
  /// via the <see cref="TypeInfoResolver"/>, not here.
  /// </summary>
  public static object? ApplyProjection(object? value, Configuration.SnapshotProjection? projection)
  {
    if (projection == null || value == null) return value;
    if (IsLeafForProjection(value)) return value;

    var projected = projection(value);

    if (projected == null) return null;
    if (IsLeafForProjection(projected)) return projected;

    if (projected is IEnumerable enumerable && projected is not string && projected is not IDictionary)
    {
      var list = new List<object?>();
      foreach (var item in enumerable)
      {
        list.Add(ApplyProjection(item, projection));
      }
      return list;
    }

    return projected;
  }

  private static bool IsLeafForProjection(object value)
  {
    if (value is string) return true;
    var type = value.GetType();
    return type.IsPrimitive || type.IsEnum;
  }
}
