using System;
using System.Globalization;
using Assertive.Config;
using Assertive.Plugin;

namespace Assertive.Runtime
{
  /// <summary>
  /// Matches runtime-registered custom patterns (Configuration.Patterns) against the
  /// structural probe recorded by the source generator, and renders the matching pattern's
  /// output templates. Ports CustomPattern's expression-tree matcher and TemplateEvaluator
  /// onto recorded facts.
  /// </summary>
  internal static class GeneratedCustomPatterns
  {
    public static (string Expected, string? Actual)? TryMatch(CustomPatternProbe probe)
    {
      if (CustomPatternRegistry.IsEmpty)
      {
        return null;
      }

      foreach (var definition in CustomPatternRegistry.GetDefinitions())
      {
        if (probe.Negated && !definition.AllowNegation)
        {
          continue;
        }

        if (!Matches(definition, probe))
        {
          continue;
        }

        var output = probe.Negated && definition.OutputWhenNegated != null
          ? definition.OutputWhenNegated
          : definition.Output;

        if (output == null)
        {
          continue;
        }

        var evaluator = BuildEvaluator(probe);

        var expected = output.Expected != null ? evaluator.Evaluate(output.Expected) : "";
        var actual = output.Actual != null ? evaluator.Evaluate(output.Actual) : null;

        return (expected, actual);
      }

      return null;
    }

    /// <summary>CustomPattern.CompileMatchers parity over the probe.</summary>
    private static bool Matches(PatternDefinition definition, CustomPatternProbe probe)
    {
      if (definition.Match is not { Length: > 0 } predicates)
      {
        return false;
      }

      var matchesMethods = false;
      var matchesProperties = false;

      foreach (var predicate in predicates)
      {
        if (predicate.Method != null)
        {
          matchesMethods = true;
          var method = predicate.Method;

          if (!probe.IsMethodCall)
          {
            return false;
          }

          if (method.Name != null && !string.Equals(probe.MemberName, method.Name, StringComparison.Ordinal))
          {
            return false;
          }

          if (method.ParameterCount.HasValue && probe.ParameterCount != method.ParameterCount.Value)
          {
            return false;
          }

          if (method.IsExtension.HasValue && probe.IsExtension != method.IsExtension.Value)
          {
            return false;
          }
        }

        if (predicate.Property != null)
        {
          matchesProperties = true;

          if (probe.IsMethodCall)
          {
            return false;
          }

          if (predicate.Property.Name != null
              && !string.Equals(probe.MemberName, predicate.Property.Name, StringComparison.Ordinal))
          {
            return false;
          }
        }

        if (predicate.DeclaringType != null)
        {
          if (probe.DeclaringType == null
              || (!string.Equals(probe.DeclaringType.Name, predicate.DeclaringType, StringComparison.Ordinal)
                  && !string.Equals(probe.DeclaringType.FullName, predicate.DeclaringType, StringComparison.Ordinal)))
          {
            return false;
          }
        }

        if (predicate.Namespace != null)
        {
          var ns = probe.DeclaringType?.Namespace;

          if (ns == null
              || (!string.Equals(ns, predicate.Namespace, StringComparison.Ordinal)
                  && !ns.StartsWith(predicate.Namespace + ".", StringComparison.Ordinal)))
          {
            return false;
          }
        }

        if (predicate.InstanceType != null)
        {
          if (probe.InstanceStaticType == null || !MatchesTypeName(probe.InstanceStaticType, predicate.InstanceType))
          {
            return false;
          }
        }
      }

      // If no method/property predicates constrained the root kind, allow both.
      if (!matchesMethods && !matchesProperties)
      {
        return true;
      }

      return probe.IsMethodCall ? matchesMethods : matchesProperties;
    }

    private static bool MatchesTypeName(Type type, string typeName)
    {
      if (string.Equals(type.Name, typeName, StringComparison.Ordinal)
          || string.Equals(type.FullName, typeName, StringComparison.Ordinal))
      {
        return true;
      }

      if (type.IsGenericType)
      {
        var genericName = type.Name;
        var backtickIndex = genericName.IndexOf('`');

        if (backtickIndex > 0 && string.Equals(genericName.Substring(0, backtickIndex), typeName, StringComparison.Ordinal))
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>TemplateEvaluator.ForMethodCall/ForPropertyAccess parity over the probe.</summary>
    private static TemplateEvaluator BuildEvaluator(CustomPatternProbe probe)
    {
      var evaluator = new TemplateEvaluator();

      if (probe.InstanceSource != null)
      {
        evaluator.Add("instance", () => Q(probe.InstanceSource));
        evaluator.Add("instance.value", () => TemplateEvaluator.FormatValue(probe.Instance?.Invoke()));
        evaluator.Add("instance.type", () => probe.InstanceStaticType != null ? TemplateEvaluator.FormatTypeName(probe.InstanceStaticType) : "?");
        evaluator.Add("instance.count", () =>
        {
          var value = probe.Instance?.Invoke();
          return value is System.Collections.IEnumerable and not string
            ? GeneratedAssert.EnumerableCount(value).ToString(CultureInfo.InvariantCulture)
            : "?";
        });
        evaluator.Add("instance.firstTenItems", () => TemplateEvaluator.FormatFirstItems(probe.Instance?.Invoke(), 10));
      }

      if (probe.IsMethodCall)
      {
        evaluator.Add("method", () => probe.MemberName);

        if (probe.ArgSources != null)
        {
          for (var i = 0; i < probe.ArgSources.Length; i++)
          {
            var index = i;
            evaluator.Add($"arg{index}", () => Q(probe.ArgSources[index]));
            evaluator.Add($"arg{index}.value", () => TemplateEvaluator.FormatValue(probe.Args?[index]?.Invoke()));
            evaluator.Add($"arg{index}.type", () =>
              probe.ArgStaticTypes?[index] is { } argType ? TemplateEvaluator.FormatTypeName(argType) : "?");
          }
        }
      }
      else
      {
        evaluator.Add("property", () => probe.MemberName);
        evaluator.Add("value", () => TemplateEvaluator.FormatValue(probe.Value?.Invoke()));
      }

      return evaluator;
    }

    /// <summary>The old path rendered sources through ExpressionToString, which applies quotation.</summary>
    private static string? Q(string? source)
    {
      if (source == null)
      {
        return null;
      }

      return Configuration.ExpressionQuotationPattern is { } pattern
        ? string.Format(CultureInfo.InvariantCulture, pattern, source)
        : source;
    }
  }
}
