using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.Plugin
{
  internal class CustomPattern : IFriendlyMessagePattern
  {
    private readonly PatternDefinition _definition;
    private readonly Func<Expression, bool> _compiledMatcher;
    private readonly bool _matchesMethodCalls;
    private readonly bool _matchesProperties;

    public CustomPattern(PatternDefinition definition)
    {
      _definition = definition;
      (_compiledMatcher, _matchesMethodCalls, _matchesProperties) = CompileMatchers(definition.Match);
    }

    /// <summary>
    /// Compiles all predicates into a single efficient matcher function.
    /// </summary>
    private static (Func<Expression, bool> matcher, bool matchesMethods, bool matchesProperties) CompileMatchers(MatchPredicate[] predicates)
    {
      if (predicates.Length == 0)
      {
        return (_ => false, false, false);
      }

      var matchers = new List<Func<Expression, bool>>();
      var matchesMethods = false;
      var matchesProperties = false;

      foreach (var predicate in predicates)
      {
        // Method matching
        if (predicate.Method != null)
        {
          matchesMethods = true;
          var method = predicate.Method;

          if (method.Name != null)
          {
            var methodName = method.Name;
            matchers.Add(expr => expr is MethodCallExpression mce &&
                                 string.Equals(mce.Method.Name, methodName, StringComparison.Ordinal));
          }

          if (method.ParameterCount.HasValue)
          {
            var count = method.ParameterCount.Value;
            matchers.Add(expr => expr is MethodCallExpression mce &&
                                 GetActualParameterCount(mce) == count);
          }

          if (method.IsExtension.HasValue)
          {
            var isExtension = method.IsExtension.Value;
            matchers.Add(expr => expr is MethodCallExpression mce &&
                                 IsExtensionMethod(mce) == isExtension);
          }
        }

        // Property matching
        if (predicate.Property != null)
        {
          matchesProperties = true;
          var property = predicate.Property;

          if (property.Name != null)
          {
            var propertyName = property.Name;
            matchers.Add(expr => expr is MemberExpression me &&
                                 me.Member is PropertyInfo &&
                                 string.Equals(me.Member.Name, propertyName, StringComparison.Ordinal));
          }
        }

        // DeclaringType (works for both methods and properties)
        if (predicate.DeclaringType != null)
        {
          var typeName = predicate.DeclaringType;
          matchers.Add(expr =>
          {
            var declaringType = GetDeclaringType(expr);
            if (declaringType == null)
            {
              return false;
            }

            return string.Equals(declaringType.Name, typeName, StringComparison.Ordinal) ||
                   string.Equals(declaringType.FullName, typeName, StringComparison.Ordinal);
          });
        }

        // Namespace
        if (predicate.Namespace != null)
        {
          var ns = predicate.Namespace;
          matchers.Add(expr =>
          {
            var declaringType = GetDeclaringType(expr);
            if (declaringType?.Namespace == null)
            {
              return false;
            }

            return string.Equals(declaringType.Namespace, ns, StringComparison.Ordinal) ||
                   declaringType.Namespace.StartsWith(ns + ".", StringComparison.Ordinal);
          });
        }

        // InstanceType
        if (predicate.InstanceType != null)
        {
          var instanceTypeName = predicate.InstanceType;
          matchers.Add(expr =>
          {
            var instanceType = GetInstanceType(expr);
            if (instanceType == null)
            {
              return false;
            }

            return MatchesTypeName(instanceType, instanceTypeName);
          });
        }
      }

      // If no specific method/property predicates were added, allow both
      if (!matchesMethods && !matchesProperties)
      {
        matchesMethods = true;
        matchesProperties = true;
      }

      if (matchers.Count == 0)
      {
        return (_ => true, matchesMethods, matchesProperties);
      }

      if (matchers.Count == 1)
      {
        return (matchers[0], matchesMethods, matchesProperties);
      }

      // Combine all matchers with AND logic
      return (expr =>
      {
        foreach (var matcher in matchers)
        {
          if (!matcher(expr))
          {
            return false;
          }
        }
        return true;
      }, matchesMethods, matchesProperties);
    }

    private static Type? GetDeclaringType(Expression expr)
    {
      return expr switch
      {
        MethodCallExpression mce => mce.Method.DeclaringType,
        MemberExpression me => me.Member.DeclaringType,
        _ => null
      };
    }

    private static Type? GetInstanceType(Expression expr)
    {
      return expr switch
      {
        MethodCallExpression mce => GetMethodCallInstanceType(mce),
        MemberExpression me => me.Expression?.Type,
        _ => null
      };
    }

    private static Type? GetMethodCallInstanceType(MethodCallExpression mce)
    {
      // For extension methods, the instance is the first argument
      if (mce.Object == null && mce.Arguments.Count > 0 && IsExtensionMethod(mce))
      {
        return mce.Arguments[0].Type;
      }
      return mce.Object?.Type;
    }

    private static bool IsExtensionMethod(MethodCallExpression mce)
    {
      return mce.Method.IsDefined(typeof(ExtensionAttribute), false);
    }

    private static int GetActualParameterCount(MethodCallExpression mce)
    {
      // For extension methods, exclude the 'this' parameter
      var count = mce.Method.GetParameters().Length;
      if (IsExtensionMethod(mce))
      {
        count--;
      }

      return count;
    }

    private static bool MatchesTypeName(Type type, string typeName)
    {
      // Match by simple name
      if (string.Equals(type.Name, typeName, StringComparison.Ordinal))
      {
        return true;
      }

      // Match by full name
      if (string.Equals(type.FullName, typeName, StringComparison.Ordinal))
      {
        return true;
      }

      // Match generic types (e.g., "List" matches List<T>)
      if (type.IsGenericType)
      {
        var genericName = type.Name;
        var backtickIndex = genericName.IndexOf('`');
        if (backtickIndex > 0)
        {
          genericName = genericName.Substring(0, backtickIndex);
          if (string.Equals(genericName, typeName, StringComparison.Ordinal))
          {
            return true;
          }
        }
      }

      return false;
    }

    public bool IsMatch(FailedAssertion failedAssertion)
    {
      var expr = failedAssertion.ExpressionWithoutNegation;

      // Check if expression type is supported by this pattern
      var isMethodCall = expr is MethodCallExpression;
      var isProperty = expr is MemberExpression me && me.Member is PropertyInfo;

      if (isMethodCall && !_matchesMethodCalls)
      {
        return false;
      }

      if (isProperty && !_matchesProperties)
      {
        return false;
      }

      if (!isMethodCall && !isProperty)
      {
        return false;
      }

      // If negated but negation not allowed, don't match
      if (failedAssertion.IsNegated && !_definition.AllowNegation)
      {
        return false;
      }

      return _compiledMatcher(expr);
    }

    public ExpectedAndActual? TryGetFriendlyMessage(FailedAssertion assertion)
    {
      var expr = assertion.ExpressionWithoutNegation;

      var output = assertion.IsNegated && _definition.OutputWhenNegated != null
        ? _definition.OutputWhenNegated
        : _definition.Output;

      if (output == null)
      {
        return null;
      }

      var evaluator = expr switch
      {
        MethodCallExpression mce => TemplateEvaluator.ForMethodCall(mce),
        MemberExpression me => TemplateEvaluator.ForPropertyAccess(me),
        _ => null
      };

      if (evaluator == null)
      {
        return null;
      }

      var expected = output.Expected != null
        ? evaluator.EvaluateToFormattable(output.Expected)
        : $"";

      var actual = output.Actual != null
        ? evaluator.EvaluateToFormattable(output.Actual)
        : null;

      return new ExpectedAndActual
      {
        Expected = expected,
        Actual = actual
      };
    }

    public IFriendlyMessagePattern[] SubPatterns => [];
  }
}
