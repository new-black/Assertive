using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assertive.Config;
using Assertive.Helpers;

namespace Assertive.Runtime
{
  /// <summary>
  /// Attributes an exception thrown during generated assertion evaluation to its cause, by
  /// walking the ExceptionStep table recorded by the source generator. Ports the
  /// expression-tree exception patterns (NullReferencePattern et al.) with identical
  /// message wording; the re-evaluating expression visitors are replaced by the steps'
  /// evaluator delegates.
  /// </summary>
  internal static class GeneratedExceptionAnalyzer
  {
    internal sealed class Handled
    {
      public Handled(string message, string? causeSource)
      {
        Message = message;
        CauseSource = causeSource;
      }

      public string Message;
      public readonly string? CauseSource;
    }

    private static readonly string NL = Environment.NewLine;

    public static Handled? Analyze(Exception exception, ExceptionStep[] steps)
    {
      // Same order as FriendlyMessageProviderForException's pattern list.
      if (exception is NullReferenceException && Walk(steps, CheckNullReference) is { } nre)
      {
        return nre;
      }

      if (exception is ArgumentNullException && Walk(steps, CheckArgumentNull) is { } argNull)
      {
        return argNull;
      }

      if (exception is InvalidOperationException && Walk(steps, CheckLinqElementCount) is { } linq)
      {
        return linq;
      }

      if (exception is IndexOutOfRangeException or ArgumentOutOfRangeException
          && Walk(steps, (s, i, x) => CheckIndexOutOfRange(s, i, x, exception)) is { } index)
      {
        return index;
      }

      if (exception is ArgumentOutOfRangeException && Walk(steps, CheckArgumentOutOfRange) is { } range)
      {
        return range;
      }

      if (exception is KeyNotFoundException && Walk(steps, CheckKeyNotFound) is { } key)
      {
        return key;
      }

      if (exception is InvalidCastException && Walk(steps, CheckInvalidCast) is { } cast)
      {
        return cast;
      }

      if (exception is FormatException && Walk(steps, CheckFormat) is { } format)
      {
        return format;
      }

      if (exception is DivideByZeroException && Walk(steps, CheckDivideByZero) is { } divide)
      {
        return divide;
      }

      return null;
    }

    private delegate Handled? StepCheck(ExceptionStep step, object? item, int index);

    private static Handled? Walk(ExceptionStep[] steps, StepCheck check) => Walk(steps, null, -1, check);

    private static Handled? Walk(ExceptionStep[] steps, object? item, int index, StepCheck check)
    {
      foreach (var step in steps)
      {
        if (step.Kind == ExceptionStepKind.LambdaIteration)
        {
          if (IterateLambda(step, item, index, check) is { } foundInsideLambda)
          {
            return foundInsideLambda;
          }

          continue;
        }

        Handled? result;

        try
        {
          result = check(step, item, index);
        }
        catch
        {
          // A step whose evaluation misbehaves is simply not the reported cause.
          continue;
        }

        if (result != null)
        {
          return result;
        }
      }

      return null;
    }

    /// <summary>
    /// The LambdaAwareExpressionVisitor equivalent: re-iterates the collection, binding the
    /// lambda item (and index) per element, and walks the lambda body's steps until the
    /// cause is found — which then gets "On item [i] of xs" context appended.
    /// </summary>
    private static Handled? IterateLambda(ExceptionStep step, object? outerItem, int outerIndex, StepCheck check)
    {
      if (step.ItemSteps == null || step.Collection == null)
      {
        return null;
      }

      object? collection;

      try
      {
        collection = step.Collection(outerItem, outerIndex);
      }
      catch
      {
        return null;
      }

      if (collection is not IEnumerable enumerable)
      {
        return null;
      }

      var index = 0;

      foreach (var item in enumerable)
      {
        if (Walk(step.ItemSteps, item, index, check) is { } found)
        {
          found.Message = $"{found.Message}{NL}{NL}On item [{index}] of {Q(step.CollectionSource)}:{NL}{Serializer.Serialize(item)}";
          return found;
        }

        index++;
      }

      return null;
    }

    private static Handled? CheckNullReference(ExceptionStep step, object? item, int index)
    {
      if (step.Kind is not (ExceptionStepKind.Member or ExceptionStepKind.ArrayLength
          or ExceptionStepKind.Call or ExceptionStepKind.Index) || step.Receiver == null)
      {
        return null;
      }

      object? receiver;

      try
      {
        receiver = step.Receiver(item, index);
      }
      catch (NullReferenceException)
      {
        // The receiver itself threw while being evaluated: the exception originated inside
        // its final member rather than from a null reference in the chain.
        return step.ReceiverLastMemberName == null
          ? null
          : new Handled($"NullReferenceException was thrown inside {E(step.ReceiverLastMemberName)} on {Q(step.ReceiverSource)}.", step.ReceiverSource);
      }

      if (receiver != null)
      {
        return null;
      }

      var message = step.Kind switch
      {
        ExceptionStepKind.ArrayLength =>
          $"NullReferenceException caused by accessing array length on {Q(step.ReceiverSource)} which was null.",
        ExceptionStepKind.Member =>
          $"NullReferenceException caused by accessing {E(step.MemberName)} on {Q(step.ReceiverSource)} which was null.",
        ExceptionStepKind.Index when step.IsArray =>
          $"NullReferenceException caused by accessing array index {Q(step.IndexSource)} on {Q(step.ReceiverSource)} which was null.",
        ExceptionStepKind.Index =>
          $"NullReferenceException caused by calling get_Item on {Q(step.ReceiverSource)} which was null.",
        _ =>
          $"NullReferenceException caused by calling {E(step.MemberName)} on {Q(step.ReceiverSource)} which was null.",
      };

      return new Handled(message, step.NodeSource);
    }

    private static Handled? CheckArgumentNull(ExceptionStep step, object? item, int index)
    {
      if (step.Kind != ExceptionStepKind.StaticCall || step.Node == null)
      {
        return null;
      }

      try
      {
        step.Node(item, index);
        return null;
      }
      catch (ArgumentNullException ex) when (ex.ParamName == "source")
      {
        return new Handled($"ArgumentNullException caused by calling {Q(step.MethodDisplay)} on {Q(step.ReceiverSource)} which was null.", step.NodeSource);
      }
      catch
      {
        return null;
      }
    }

    private static Handled? CheckLinqElementCount(ExceptionStep step, object? item, int index)
    {
      if (!step.IsLinqElementMethod || step.Node == null || step.Receiver == null)
      {
        return null;
      }

      try
      {
        step.Node(item, index);
        return null;
      }
      catch (InvalidOperationException ex) when (!ex.Message.StartsWith("Assertive:", StringComparison.Ordinal))
      {
      }
      catch
      {
        return null;
      }

      if (step.Receiver(item, index) is not IEnumerable instance)
      {
        return null;
      }

      if (step.Filtered && step.Filter == null)
      {
        return null;
      }

      var count = 0;

      foreach (var element in instance)
      {
        if (!step.Filtered || step.Filter!(element, item, index))
        {
          count++;
        }
      }

      var tooFew = count == 0 && step.MemberName is "Single" or "First";
      var tooMany = count > 1 && step.MemberName is "Single" or "SingleOrDefault";

      if (!tooFew && !tooMany)
      {
        return null;
      }

      var message = tooFew
        ? $"InvalidOperationException caused by calling {Q(step.MethodDisplay)} on {Q(step.ReceiverSource)} which contains no elements{(step.Filtered ? " that match the filter" : "")}."
        : $"InvalidOperationException caused by calling {Q(step.MethodDisplay)} on {Q(step.ReceiverSource)} which contains more than one element{(step.Filtered ? " that matches the filter" : "")}. Actual element count: {count}.";

      if ((tooFew && step.Filtered) || tooMany)
      {
        // TooMany lists the filter-matching items; a filtered TooFew lists everything
        // (mirrors LinqElementCountPattern.GetItems).
        var listFilter = step.Filtered && tooMany ? step.Filter : null;
        var items = new TruncatedList(instance is ICollection collection ? collection.Count : (int?)null);

        foreach (var element in instance)
        {
          if (listFilter != null && !listFilter(element, item, index))
          {
            continue;
          }

          items.Add(element!);

          if (items.Count > 10)
          {
            break;
          }
        }

        message = $"{message}{NL}{NL}Value of {Q(step.ReceiverSource)}: {Serializer.Serialize(items)}";
      }

      return new Handled(message, step.NodeSource);
    }

    private static Handled? CheckIndexOutOfRange(ExceptionStep step, object? item, int index, Exception exception)
    {
      if (step.Kind != ExceptionStepKind.Index || step.IsDictionary
          || step.Receiver == null || step.Index == null)
      {
        return null;
      }

      var receiver = step.Receiver(item, index);

      if (step.Index(item, index) is not IConvertible rawIndex)
      {
        return null;
      }

      var indexValue = rawIndex.ToInt32(CultureInfo.InvariantCulture);

      int length;
      string lengthString;

      if (step.IsArray && exception is IndexOutOfRangeException)
      {
        if (receiver is not Array array)
        {
          return null;
        }

        length = array.Length;
        lengthString = "length";
      }
      else if (!step.IsArray && exception is ArgumentOutOfRangeException)
      {
        if (receiver is not IEnumerable enumerable)
        {
          return null;
        }

        length = GeneratedAssert.EnumerableCount(enumerable);
        lengthString = "count";
      }
      else
      {
        return null;
      }

      if (indexValue >= 0 && indexValue < length)
      {
        return null;
      }

      var indexString = step.IndexIsConstant
        ? Q(step.IndexSource)
        : $"{Q(step.IndexSource)} (value: {Serializer.Serialize(indexValue)})";

      return new Handled(
        $"{exception.GetType().Name} caused by accessing index {indexString} on {Q(step.ReceiverSource)}, actual {lengthString} was {length}.",
        step.NodeSource);
    }

    private static Handled? CheckArgumentOutOfRange(ExceptionStep step, object? item, int index)
    {
      if (step.Kind is not (ExceptionStepKind.Call or ExceptionStepKind.StaticCall) || step.Node == null)
      {
        return null;
      }

      try
      {
        step.Node(item, index);
        return null;
      }
      catch (ArgumentOutOfRangeException)
      {
      }
      catch
      {
        return null;
      }

      var argsString = BuildArgumentsString(step, item, index);

      if (step.Kind == ExceptionStepKind.StaticCall)
      {
        return new Handled($"ArgumentOutOfRangeException caused by calling {step.StaticTypeName}.{step.MemberName}({argsString}).", step.NodeSource);
      }

      string? instanceValue = null;

      try
      {
        instanceValue = step.Receiver?.Invoke(item, index) as string;
      }
      catch
      {
        // Could not evaluate.
      }

      var message = instanceValue != null && step.MemberName == "Substring"
        ? $"ArgumentOutOfRangeException caused by calling {step.MemberName}({argsString}) on {E(step.ReceiverSource)} (length: {instanceValue.Length})."
        : instanceValue != null
          ? $"ArgumentOutOfRangeException caused by calling {step.MemberName}({argsString}) on {E(step.ReceiverSource)}. Value of {E(step.ReceiverSource)}: {Serializer.Serialize(instanceValue)}"
          : $"ArgumentOutOfRangeException caused by calling {step.MemberName}({argsString}) on {E(step.ReceiverSource)}.";

      return new Handled(message, step.NodeSource);
    }

    private static string BuildArgumentsString(ExceptionStep step, object? item, int index)
    {
      if (step.ArgSources == null)
      {
        return "";
      }

      var parts = new List<string>();

      for (var i = 0; i < step.ArgSources.Length; i++)
      {
        var source = step.ArgSources[i] ?? "";
        object? value = null;
        var evaluated = false;

        try
        {
          if (step.Args?[i] is { } evaluator)
          {
            value = evaluator(item, index);
            evaluated = true;
          }
        }
        catch
        {
          // Could not evaluate.
        }

        if (step.ArgIsConstant?[i] == true)
        {
          parts.Add(evaluated ? Serializer.Serialize(value).ToString() : E(source));
        }
        else
        {
          parts.Add(evaluated ? $"{E(source)} (value: {Serializer.Serialize(value)})" : E(source));
        }
      }

      return string.Join(", ", parts);
    }

    private static Handled? CheckKeyNotFound(ExceptionStep step, object? item, int index)
    {
      if (step.Kind != ExceptionStepKind.Index || !step.IsDictionary
          || step.Receiver == null || step.Index == null)
      {
        return null;
      }

      object? keyValue = null;

      try
      {
        keyValue = step.Index(item, index);
      }
      catch
      {
        // Could not evaluate key.
      }

      string? availableKeys = null;
      IDictionary? dictionary = null;

      try
      {
        dictionary = step.Receiver(item, index) as IDictionary;
      }
      catch
      {
        // Could not evaluate dictionary.
      }

      if (dictionary != null)
      {
        if (keyValue == null || dictionary.Contains(keyValue))
        {
          return null;
        }

        var keys = dictionary.Keys.Cast<object>().Take(10).ToList();
        var hasMore = dictionary.Count > 10;
        availableKeys = keys.Count == 0
          ? "(empty)"
          : string.Join(", ", keys.Select(k => Serializer.Serialize(k).ToString())) + (hasMore ? ", ..." : "");
      }
      else
      {
        return null;
      }

      var keyString = step.IndexIsConstant
        ? Serializer.Serialize(keyValue).ToString()
        : $"{E(step.IndexSource)} (value: {Serializer.Serialize(keyValue)})";

      return new Handled(
        $"KeyNotFoundException caused by accessing key {keyString} on {Q(step.ReceiverSource)}. Available keys: {availableKeys}.",
        step.NodeSource);
    }

    private static Handled? CheckInvalidCast(ExceptionStep step, object? item, int index)
    {
      if (step.Kind != ExceptionStepKind.Cast || step.Receiver == null || step.TargetType == null)
      {
        return null;
      }

      var operand = step.Receiver(item, index);

      if (operand == null || step.TargetType.IsInstanceOfType(operand))
      {
        return null;
      }

      return new Handled(
        $"InvalidCastException caused by casting {E(step.ReceiverSource)} to {TypeHelper.TypeNameToString(step.TargetType)}. Actual type was {TypeHelper.TypeNameToString(operand.GetType())}.",
        step.NodeSource);
    }

    private static Handled? CheckFormat(ExceptionStep step, object? item, int index)
    {
      if (!step.IsParsingMethod || step.Node == null)
      {
        return null;
      }

      try
      {
        step.Node(item, index);
        return null;
      }
      catch (FormatException)
      {
      }
      catch
      {
        return null;
      }

      string inputString;
      string? inputValue = null;

      try
      {
        if (step.StringArgIndex >= 0 && step.Args?[step.StringArgIndex] is { } evaluator)
        {
          inputValue = evaluator(item, index) as string;
        }
      }
      catch
      {
        // Could not evaluate.
      }

      if (inputValue != null)
      {
        inputString = Serializer.Serialize(inputValue).ToString();
      }
      else if (step.StringArgIndex >= 0 && step.ArgSources?[step.StringArgIndex] is { } source)
      {
        inputString = E(source);
      }
      else
      {
        inputString = "unknown";
      }

      var message = step.Kind == ExceptionStepKind.StaticCall
        ? $"FormatException caused by calling {step.StaticTypeName}.{step.MemberName}({inputString}). {inputString} is not a valid {step.ParseTargetTypeName}."
        : $"FormatException caused by calling {step.MemberName}({inputString}) on {E(step.ReceiverSource)}.";

      return new Handled(message, step.NodeSource);
    }

    private static Handled? CheckDivideByZero(ExceptionStep step, object? item, int index)
    {
      if (step.Kind is not (ExceptionStepKind.Divide or ExceptionStepKind.Modulo) || step.Right == null)
      {
        return null;
      }

      var divisor = step.Right(item, index);

      bool isZero;

      try
      {
        isZero = divisor is IConvertible convertible && convertible.ToDouble(CultureInfo.InvariantCulture) == 0;
      }
      catch
      {
        return null;
      }

      if (!isZero)
      {
        return null;
      }

      var operation = step.Kind == ExceptionStepKind.Divide ? "dividing" : "modulo";
      var divisorString = step.RightIsConstant ? "0" : $"{E(step.RightSource)} (value: 0)";

      return new Handled($"DivideByZeroException caused by {operation} {E(step.LeftSource)} by {divisorString}.", step.NodeSource);
    }

    /// <summary>
    /// Applies the configured expression quotation pattern and C# syntax highlighting
    /// (slots that were Expression arguments in the old FormattableString messages were
    /// quoted and highlighted by the formatter via ExpressionToString).
    /// </summary>
    private static string Q(string? source)
    {
      if (source == null)
      {
        return "";
      }

      var quoted = Configuration.ExpressionQuotationPattern is { } pattern
        ? string.Format(CultureInfo.InvariantCulture, pattern, source)
        : source;

      return Configuration.Colors.Expression(quoted);
    }

    /// <summary>
    /// Syntax highlighting without quotation, for slots the old patterns rendered with
    /// ExpressionToString(allowQuotation: false) or wrapped in Colors.Expression directly.
    /// </summary>
    private static string E(string? source)
    {
      return source == null ? "" : Configuration.Colors.Expression(source);
    }
  }
}
