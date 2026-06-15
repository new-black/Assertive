using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Assertive.Helpers;

namespace Assertive.Runtime
{
  /// <summary>
  /// Infrastructure for code emitted by Assertive's source generator. Not intended to be
  /// called directly from user code.
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  [System.Diagnostics.StackTraceHidden]
  public static class GeneratedAssert
  {
    /// <summary>
    /// Whether the exception is an Assertive assertion failure (as opposed to an exception
    /// thrown while evaluating the assertion). Used by generated exception filters.
    /// </summary>
    public static bool IsAssertionFailure(Exception exception) => exception.Data.Contains("Assertive.Expected");

    /// <summary>
    /// Shared scaffolding for generated interceptors: the delegate decides pass/fail —
    /// exactly one evaluation — and only on failure (or an evaluation exception, passed as
    /// the callback's argument) does the render callback build the decomposed report.
    /// Reporting is best-effort: decomposition reads closures and members reflectively,
    /// which can fail when reflection metadata was trimmed away (Native AOT); it then
    /// falls back to the source-text report instead of leaking an infrastructure exception.
    /// </summary>
    public static void Execute(Func<bool> assertion, string assertionExpression, object? message,
      Func<object?>? context, string? contextExpression, Func<Exception?, Exception> render)
    {
      bool passed;

      try
      {
        passed = assertion();
      }
      catch (Exception ex) when (!IsAssertionFailure(ex))
      {
        Exception report;

        try
        {
          report = render(ex);
        }
        catch (Exception rex) when (!IsAssertionFailure(rex))
        {
          report = EvaluationFailure(assertionExpression, ex, null, null, message, context, contextExpression);
        }

        throw report;
      }

      if (passed)
      {
        return;
      }

      Exception failure;

      try
      {
        failure = render(null);
      }
      catch (Exception rex) when (!IsAssertionFailure(rex))
      {
        failure = Failure(assertionExpression, null, message, context, contextExpression);
      }

      throw failure;
    }

    /// <summary>
    /// Async counterpart of <see cref="Execute(Func{bool}, string, object, Func{object}, string, Func{Exception, Exception})"/>
    /// for Func&lt;Task&lt;bool&gt;&gt; assertions: the render callback is asynchronous because
    /// rebuilding the report re-evaluates operands, which may themselves await.
    /// </summary>
    public static async System.Threading.Tasks.Task Execute(Func<System.Threading.Tasks.Task<bool>> assertion,
      string assertionExpression, object? message, Func<object?>? context, string? contextExpression,
      Func<Exception?, System.Threading.Tasks.Task<Exception>> render)
    {
      bool passed;

      try
      {
        passed = await assertion();
      }
      catch (Exception ex) when (!IsAssertionFailure(ex))
      {
        Exception report;

        try
        {
          report = await render(ex);
        }
        catch (Exception rex) when (!IsAssertionFailure(rex))
        {
          report = EvaluationFailure(assertionExpression, ex, null, null, message, context, contextExpression);
        }

        throw report;
      }

      if (passed)
      {
        return;
      }

      Exception failure;

      try
      {
        failure = await render(null);
      }
      catch (Exception rex) when (!IsAssertionFailure(rex))
      {
        failure = Failure(assertionExpression, null, message, context, contextExpression);
      }

      throw failure;
    }

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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort read of a captured local from a closure for failure messages; if the closure's fields are trimmed the read fails and the assertion degrades to reporting its source text.")]
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort read of the captured 'this' from a closure for failure messages; if the closure's fields are trimmed the read fails and the assertion degrades to reporting its source text.")]
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort reflective method invocation for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
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

    /// <summary>
    /// Invokes a static method by name, ignoring accessibility. The static counterpart of
    /// <see cref="InvokeInstance"/>, for methods generated code cannot call directly
    /// (private helpers); the generator guarantees the name + argument count resolve to a
    /// single method. Exceptions thrown by the method are rethrown unwrapped.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Best-effort reflective decomposition for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort reflective decomposition for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
    public static object? InvokeStatic(Type type, string methodName, object?[] arguments)
    {
      for (var current = type; current != null; current = current.BaseType)
      {
        MethodInfo? match = null;

        foreach (var method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
          if (method.Name == methodName && method.GetParameters().Length == arguments.Length)
          {
            if (match != null)
            {
              throw new InvalidOperationException($"Assertive: static method '{methodName}' with {arguments.Length} parameter(s) is ambiguous on {current}.");
            }

            match = method;
          }
        }

        if (match != null)
        {
          try
          {
            return match.Invoke(null, arguments);
          }
          catch (TargetInvocationException ex) when (ex.InnerException != null)
          {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
          }
        }
      }

      throw new InvalidOperationException($"Assertive: could not resolve static method '{methodName}' with {arguments.Length} parameter(s) on {type}.");
    }

    /// <summary>Reads an instance field or property by name, ignoring accessibility.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort reflective member read for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Best-effort reflective evaluation of a parameterless LINQ operator; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "MakeGenericMethod over the element type is best-effort; under AOT it may throw and the assertion degrades to reporting its source text.")]
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Best-effort element-type discovery for reflective LINQ evaluation; trimmed interface metadata simply yields no element type and the assertion degrades to reporting its source text.")]
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Best-effort reflective decomposition for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
    public static Type GetNestedType(Type parent, string name)
    {
      return parent.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Assertive: could not resolve nested type '{name}' on {parent}.");
    }

    /// <summary>Reads a static field or property (including enum constants) by name, ignoring accessibility.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Best-effort reflective decomposition for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort reflective decomposition for failure messages; trimmed metadata makes the lookup fail and the assertion degrades to reporting its source text.")]
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

    /// <summary>
    /// Combines the failing leaves of a logically-composed assertion (`a &amp; b`, `a || b`,
    /// ...) into one failure, each leaf reporting as its own assertion. The generated
    /// flags chain evaluates every leaf and collects the failing ones; `&amp;&amp;`-only bodies
    /// use a short-circuit chain instead, where only one leaf can fail.
    /// </summary>
    public static Exception CombinedFailure(List<Exception> failures)
    {
      if (failures.Count == 1)
      {
        return failures[0];
      }

      var combined = ExceptionHelper.GetException(string.Join(Environment.NewLine, failures.Select(f => f.Message)));

      combined.Data["Assertive.Expected"] = ConcatData(failures, "Assertive.Expected");
      combined.Data["Assertive.Actual"] = ConcatData(failures, "Assertive.Actual");
      combined.Data["Assertive.HandledExceptions"] = ConcatData(failures, "Assertive.HandledExceptions");

      return combined;
    }

    private static string[] ConcatData(List<Exception> failures, string key)
    {
      return failures.SelectMany(f => f.Data[key] as string[] ?? Array.Empty<string>()).ToArray();
    }

    /// <summary>
    /// Consults the runtime-registered custom patterns (Configuration.Patterns) with the
    /// recorded structural probe. Returns the custom failure when a pattern matches —
    /// custom patterns take precedence over the built-in decomposition — or null.
    /// </summary>
    public static Exception? TryCustomFailure(
      string assertionExpression,
      CustomPatternProbe probe,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      (string Expected, string? Actual)? match;

      try
      {
        match = GeneratedCustomPatterns.TryMatch(probe);
      }
      catch
      {
        return null;
      }

      if (match == null)
      {
        return null;
      }

      return AssertionFailureBuilder.Build(new AssertionFailureBuilder.FailureDetails
      {
        AssertionText = AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        Expected = match.Value.Expected,
        Actual = match.Value.Actual,
        Locals = locals,
        UserMessage = message,
        Context = context,
        ContextExpression = contextExpression,
      });
    }

    /// <summary>A failed All() call, decomposed by the generator (AllPattern/NotAllPattern parity).</summary>
    public static Exception AllFailure(
      string assertionExpression,
      string collectionSource,
      string filterSource,
      bool collectionIsMethodCall,
      object? collectionValue,
      bool negated,
      Func<object?, int, bool>? filter,
      Func<object?, int, Exception>? subFailure,
      (string Name, object? Value)[] locals,
      object? message,
      Func<object?>? context,
      string? contextExpression)
    {
      return AssertionFailureBuilder.BuildAll(
        AssertionFailureBuilder.StripLambdaPrefix(assertionExpression) ?? assertionExpression,
        collectionSource, filterSource, collectionIsMethodCall, collectionValue, negated,
        filter, subFailure, locals, message, context, contextExpression);
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
    /// Intercepted Assert.Throws with an exception predicate: same semantics as the public
    /// overloads, plus a generated factory that decomposes the predicate body (with the
    /// thrown exception bound to its parameter) when the predicate fails.
    /// </summary>
    public static Exception ThrowsIntercepted(
      Action action,
      Type? expectedExceptionType,
      Func<Exception, bool>? exceptionAssertion,
      string actionExpression,
      string? exceptionExpression,
      Func<object?, int, Exception>? predicateFailure)
    {
      var result = AssertImpl.Throws(action, actionExpression, expectedExceptionType, exceptionAssertion, exceptionExpression, predicateFailure);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>Async counterpart of ThrowsIntercepted.</summary>
    public static async System.Threading.Tasks.Task<Exception> ThrowsInterceptedAsync(
      Func<System.Threading.Tasks.Task> action,
      Type? expectedExceptionType,
      Func<Exception, bool>? exceptionAssertion,
      string actionExpression,
      string? exceptionExpression,
      Func<object?, int, Exception>? predicateFailure)
    {
      var result = await AssertImpl.Throws(action, actionExpression, expectedExceptionType, exceptionAssertion, exceptionExpression, predicateFailure);

      if (result.Failure != null)
      {
        throw result.Failure;
      }

      return result.Thrown!;
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to a compact single-line string suitable for embedding
    /// in failure messages (e.g. mock invocation argument display). Uses the same serializer as
    /// Assertive's own failure rendering, giving rich output for records, collections, and plain
    /// classes with public properties.
    /// </summary>
    public static string SerializeValue(object? value) => Serializer.SerializeInline(value);

    /// <summary>Equals-based equality for reflective filter evaluation over object-typed operands.</summary>
    public static bool ObjectEquals(object? left, object? right) => Equals(left, right);

    /// <summary>
    /// Awaits an object-typed Task and reads its result reflectively, for reflective
    /// re-evaluation of awaited operands (where the result type cannot be named in
    /// generated code). Non-generic tasks yield null.
    /// </summary>
    public static async System.Threading.Tasks.Task<object?> AwaitResult(object awaitable)
    {
      if (awaitable is not System.Threading.Tasks.Task task)
      {
        throw new InvalidOperationException($"Assertive: cannot reflectively await a {awaitable?.GetType().ToString() ?? "null"}; only Task-based awaitables are supported.");
      }

      await task.ConfigureAwait(false);

      var taskType = task.GetType();

      if (!taskType.IsGenericType)
      {
        return null;
      }

      // Throw rather than silently yield null when the property metadata was trimmed away
      // (Native AOT): the render callback's failure falls back to the source-text report.
      var resultProperty = taskType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Assertive: could not read the result of a {taskType} reflectively.");

      return resultProperty.PropertyType.Name != "VoidTaskResult"
        ? resultProperty.GetValue(task)
        : null;
    }

    /// <summary>Reads an element by index/key, ignoring accessibility (arrays, lists, indexers).</summary>
    public static object? GetElementValue(object target, object? index)
    {
      if (target is Array array && index is { } arrayIndex)
      {
        return array.GetValue(Convert.ToInt64(arrayIndex));
      }

      if (target is System.Collections.IList list && index is int listIndex)
      {
        return list[listIndex];
      }

      return InvokeInstance(target, "get_Item", new[] { index });
    }

    /// <summary>Count(predicate) over an untyped enumerable (reflective evaluation).</summary>
    public static int EnumerableCount(object source, Func<object?, bool> predicate)
    {
      var count = 0;

      foreach (var item in (System.Collections.IEnumerable)source)
      {
        if (predicate(item))
        {
          count++;
        }
      }

      return count;
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
