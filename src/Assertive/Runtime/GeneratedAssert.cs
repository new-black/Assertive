using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Assertive.Runtime
{
  /// <summary>
  /// Infrastructure for code emitted by Assertive's source generator. Not intended to be
  /// called directly from user code.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedAssert
  {
    private static long _interceptedCallCount;

    /// <summary>
    /// The number of Assert.That calls that were served by a generated interceptor in this
    /// process. Exists for diagnostics and for tests that need to prove interception is active.
    /// </summary>
    public static long InterceptedCallCount => Interlocked.Read(ref _interceptedCallCount);

    public static void MarkIntercepted() => Interlocked.Increment(ref _interceptedCallCount);

    /// <summary>
    /// Whether the exception is an Assertive assertion failure (as opposed to an exception
    /// thrown while evaluating the assertion). Used by generated exception filters.
    /// </summary>
    public static bool IsAssertionFailure(Exception exception) => exception.Data.Contains("Assertive.Expected");

    /// <summary>
    /// Extracts the value of a captured local variable or parameter from the assertion
    /// delegate's closure. The compiler stores captured variables as public fields named
    /// after the variable on compiler-generated display classes; captures from multiple
    /// scopes form a chain of display classes, which is searched breadth-first.
    /// </summary>
    public static object? GetCapturedValue(Delegate assertion, string name)
    {
      var target = assertion.Target
        ?? throw new InvalidOperationException($"Assertive: the assertion delegate has no closure to read '{name}' from.");

      if (TryGetCapturedValue(target, name, depth: 0, out var value))
      {
        return value;
      }

      throw new InvalidOperationException($"Assertive: could not locate captured variable '{name}' in the assertion's closure.");
    }

    private static bool TryGetCapturedValue(object closure, string name, int depth, out object? value)
    {
      var type = closure.GetType();
      var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

      if (field != null)
      {
        value = field.GetValue(closure);
        return true;
      }

      if (depth < 4)
      {
        // Captures from enclosing scopes live on chained display-class instances.
        foreach (var chained in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
          if (chained.FieldType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
              && chained.GetValue(closure) is { } next
              && TryGetCapturedValue(next, name, depth + 1, out value))
          {
            return true;
          }
        }
      }

      value = null;
      return false;
    }

    /// <summary>
    /// Extracts the enclosing instance (`this`) captured by the assertion delegate. When the
    /// lambda captures only `this`, the delegate's target is the instance itself; when it
    /// also captures locals, the instance lives in a `&lt;&gt;4__this` field on the display class.
    /// </summary>
    public static object GetCapturedThis(Delegate assertion)
    {
      var target = assertion.Target
        ?? throw new InvalidOperationException("Assertive: the assertion delegate does not capture 'this'.");

      if (!target.GetType().IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
      {
        return target;
      }

      if (TryGetCapturedThis(target, depth: 0, out var value) && value != null)
      {
        return value;
      }

      throw new InvalidOperationException("Assertive: could not locate the captured 'this' reference in the assertion's closure.");
    }

    private static bool TryGetCapturedThis(object closure, int depth, out object? value)
    {
      var fields = closure.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

      foreach (var field in fields)
      {
        if (field.Name.EndsWith("__this", StringComparison.Ordinal))
        {
          value = field.GetValue(closure);
          return true;
        }
      }

      if (depth < 4)
      {
        foreach (var chained in fields)
        {
          if (chained.FieldType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
              && chained.GetValue(closure) is { } next
              && TryGetCapturedThis(next, depth + 1, out value))
          {
            return true;
          }
        }
      }

      value = null;
      return false;
    }

    /// <summary>
    /// Invokes an instance method by name, ignoring accessibility. Used by generated code
    /// when the member or the types involved cannot be named in generated source (private
    /// members, private nested types); the generator guarantees the name + argument count
    /// resolve to a single method. Exceptions thrown by the method are rethrown unwrapped.
    /// </summary>
    public static object? InvokeInstance(object target, string methodName, object?[] arguments)
    {
      for (var type = target.GetType(); type != null; type = type.BaseType)
      {
        MethodInfo? match = null;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
          if (method.Name == methodName && method.GetParameters().Length == arguments.Length)
          {
            if (match != null)
            {
              throw new InvalidOperationException($"Assertive: method '{methodName}' with {arguments.Length} parameter(s) is ambiguous on {type}.");
            }

            match = method;
          }
        }

        if (match != null)
        {
          try
          {
            return match.Invoke(target, arguments);
          }
          catch (TargetInvocationException ex) when (ex.InnerException != null)
          {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
          }
        }
      }

      throw new InvalidOperationException($"Assertive: could not resolve method '{methodName}' with {arguments.Length} parameter(s) on {target.GetType()}.");
    }

    /// <summary>Reads an instance field or property by name, ignoring accessibility.</summary>
    public static object? GetMemberValue(object target, string memberName)
    {
      const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

      for (var type = target.GetType(); type != null; type = type.BaseType)
      {
        if (type.GetField(memberName, flags) is { } field)
        {
          return field.GetValue(target);
        }

        if (type.GetProperty(memberName, flags) is { } property)
        {
          try
          {
            return property.GetValue(target);
          }
          catch (TargetInvocationException ex) when (ex.InnerException != null)
          {
            // A throwing getter should look like a direct member access.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
          }
        }
      }

      throw new InvalidOperationException($"Assertive: could not resolve member '{memberName}' on {target.GetType()}.");
    }

    /// <summary>
    /// Invokes a parameterless System.Linq.Enumerable extension method (First, Single, ...)
    /// on an untyped source, for reflective evaluation when the element type cannot be
    /// named in generated code. Exceptions thrown by the method are rethrown unwrapped.
    /// </summary>
    public static object? InvokeLinq(object source, string methodName)
    {
      var elementType = GetEnumerableElementType(source.GetType())
        ?? throw new InvalidOperationException($"Assertive: {source.GetType()} is not an IEnumerable<T>.");

      MethodInfo? match = null;

      foreach (var method in typeof(System.Linq.Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static))
      {
        if (method.Name == methodName && method.GetParameters().Length == 1 && method.IsGenericMethodDefinition)
        {
          match = method;
          break;
        }
      }

      if (match == null)
      {
        throw new InvalidOperationException($"Assertive: could not resolve Enumerable.{methodName} with a single parameter.");
      }

      try
      {
        return match.MakeGenericMethod(elementType).Invoke(null, new[] { source });
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
      }
    }

    private static Type? GetEnumerableElementType(Type type)
    {
      if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
      {
        return type.GetGenericArguments()[0];
      }

      foreach (var iface in type.GetInterfaces())
      {
        if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
          return iface.GetGenericArguments()[0];
        }
      }

      return null;
    }

    /// <summary>Resolves a nested type by metadata name, ignoring accessibility.</summary>
    public static Type GetNestedType(Type parent, string name)
    {
      return parent.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Assertive: could not resolve nested type '{name}' on {parent}.");
    }

    /// <summary>Reads a static field or property (including enum constants) by name, ignoring accessibility.</summary>
    public static object? GetStaticMemberValue(Type type, string memberName)
    {
      const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

      for (var current = type; current != null; current = current.BaseType)
      {
        if (current.GetField(memberName, flags) is { } field)
        {
          return field.GetValue(null);
        }

        if (current.GetProperty(memberName, flags) is { } property)
        {
          return property.GetValue(null);
        }
      }

      throw new InvalidOperationException($"Assertive: could not resolve static member '{memberName}' on {type}.");
    }

    /// <summary>Throws-side entry: a failed (non-exceptional) assertion with no pattern decomposition.</summary>
    public static Exception Failure(string assertionExpression, (string Name, object? Value)[]? locals, object? message, Func<object?>? context, string? contextExpression)
    {
      return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
      {
        AssertionText = AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        Locals = locals,
        UserMessage = message,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>
    /// An exception thrown while evaluating the assertion. When the generator recorded
    /// exception steps for the call site, the cause is attributed by walking them
    /// (exception-pattern parity); otherwise the exception is reported as-is.
    /// </summary>
    public static Exception EvaluationFailure(string assertionExpression, Exception exception,
      ExceptionStep[]? steps, (string Name, object? Value)[]? locals,
      object? message, Func<object?>? context, string? contextExpression)
    {
      GeneratedExceptionAnalyzer.Handled? handled = null;

      if (steps is { Length: > 0 })
      {
        try
        {
          handled = GeneratedExceptionAnalyzer.Analyze(exception, steps);
        }
        catch
        {
          // Attribution is best-effort; fall back to the plain exception report.
        }
      }

      return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
      {
        AssertionText = AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        Exception = exception,
        HandledExceptionMessage = handled?.Message,
        CauseSource = handled?.CauseSource,
        Locals = locals,
        UserMessage = message,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>A failed ==/!= assertion, decomposed by the generator (EqualsPattern parity).</summary>
    public static Exception EqualityFailure(
      string assertionExpression,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildEquality(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        leftSource,
        leftValue,
        rightSource,
        rightValue,
        rightIsConstant,
        negated,
        locals,
        message,
        context,
        contextExpression);
    }

    /// <summary>A failed ReferenceEquals assertion, decomposed by the generator (ReferenceEqualsPattern parity).</summary>
    public static Exception ReferenceEqualsFailure(
      string assertionExpression,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildReferenceEquals(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        leftSource,
        leftValue,
        rightSource,
        rightValue,
        negated,
        locals,
        message,
        context,
        contextExpression);
    }

    /// <summary>A failed bare-bool assertion, decomposed by the generator (BoolPattern parity).</summary>
    public static Exception BoolFailure(
      string assertionExpression,
      string source,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildBool(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        source, negated, locals, message, context, contextExpression);
    }

    /// <summary>A failed null check, decomposed by the generator (NullPattern parity).</summary>
    public static Exception NullFailure(
      string assertionExpression,
      string source,
      object? value,
      bool expectedNull,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildNull(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        source, value, expectedNull, locals, message, context, contextExpression);
    }

    /// <summary>A failed HasValue check, decomposed by the generator (HasValuePattern parity).</summary>
    public static Exception HasValueFailure(
      string assertionExpression,
      string source,
      object? value,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildHasValue(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        source, value, negated, locals, message, context, contextExpression);
    }

    /// <summary>A failed `is T` check, decomposed by the generator (IsPattern parity).</summary>
    public static Exception IsTypeFailure(
      string assertionExpression,
      string source,
      object? value,
      Type expectedType,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildIsType(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        source, value, expectedType, negated, locals, message, context, contextExpression);
    }

    /// <summary>A failed &lt;/&lt;=/&gt;/&gt;= comparison, decomposed by the generator (LessThanOrGreaterThanPattern parity).</summary>
    public static Exception ComparisonFailure(
      string assertionExpression,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      string comparisonLabel,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildComparison(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        leftSource, leftValue, rightSource, rightValue, rightIsConstant, comparisonLabel,
        locals, message, context, contextExpression);
    }

    /// <summary>A failed Length/Count comparison, decomposed by the generator (LengthPattern parity).</summary>
    public static Exception LengthFailure(
      string assertionExpression,
      string operandSource,
      string? filterSource,
      string countLabel,
      string comparisonLabel,
      object? actualLength,
      string rightSource,
      object? rightValue,
      bool rightIsConstant,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildLength(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        operandSource, filterSource, countLabel, comparisonLabel, actualLength,
        rightSource, rightValue, rightIsConstant, locals, message, context, contextExpression);
    }

    /// <summary>A failed Contains call, decomposed by the generator (ContainsPattern parity).</summary>
    public static Exception ContainsFailure(
      string assertionExpression,
      string instanceSource,
      object? instanceValue,
      string expectedSource,
      object? expectedValue,
      bool expectedIsConstant,
      bool stringInstance,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildContains(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        instanceSource, instanceValue, expectedSource, expectedValue, expectedIsConstant,
        stringInstance, negated, locals, message, context, contextExpression);
    }

    /// <summary>A failed StartsWith/EndsWith call, decomposed by the generator (StartsWithAndEndsWithPattern parity).</summary>
    public static Exception StartsEndsWithFailure(
      string assertionExpression,
      string instanceSource,
      object? instanceValue,
      string argSource,
      object? argValue,
      bool argIsConstant,
      string methodLabel,
      bool negated,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildStartsEndsWith(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        instanceSource, instanceValue, argSource, argValue, argIsConstant,
        methodLabel, negated, locals, message, context, contextExpression);
    }

    /// <summary>A failed Any() call, decomposed by the generator (AnyPattern parity).</summary>
    public static Exception AnyFailure(
      string assertionExpression,
      string collectionSource,
      string? filterSource,
      bool negated,
      int count,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildAny(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        collectionSource, filterSource, negated, count, locals, message, context, contextExpression);
    }

    /// <summary>A failed SequenceEqual call, decomposed by the generator (SequenceEqualPattern parity).</summary>
    public static Exception SequenceEqualFailure(
      string assertionExpression,
      string leftSource,
      object? leftValue,
      string rightSource,
      object? rightValue,
      object? comparer,
      Type? elementType,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildSequenceEqual(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        leftSource, leftValue, rightSource, rightValue, comparer, elementType,
        locals, message, context, contextExpression);
    }

    /// <summary>
    /// Count() over an untyped enumerable, for reflective operand evaluation when the
    /// element type cannot be named in generated code.
    /// </summary>
    public static int EnumerableCount(object source)
    {
      if (source is System.Collections.ICollection collection)
      {
        return collection.Count;
      }

      var count = 0;

      foreach (var _ in (System.Collections.IEnumerable)source)
      {
        count++;
      }

      return count;
    }
  }
}
