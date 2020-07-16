using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

using static Assertive.Expressions.ExpressionStringBuilder;

namespace Assertive.ExceptionPatterns
{
  internal class LinqElementCountPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is InvalidOperationException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var linqVisitor = new LinqElementCountVisitor();

      linqVisitor.Visit(assertion.Expression);

      if (linqVisitor.CauseOfLinqException != null)
      {
        var filtered = linqVisitor.CauseOfLinqException.Arguments.Count == 2 &&
                       linqVisitor.CauseOfLinqException.Arguments[1] is LambdaExpression;
        
        var instanceOfMethodCallExpression = ExpressionHelper.GetInstanceOfMethodCall(linqVisitor.CauseOfLinqException);

        var message = GetReasonMessage(linqVisitor.CauseOfLinqException, linqVisitor.Error, linqVisitor.ActualCount,
          filtered, instanceOfMethodCallExpression);

        if ((linqVisitor.Error == LinqElementCountErrorTypes.TooFew && filtered) 
            || linqVisitor.Error == LinqElementCountErrorTypes.TooMany)
        { 
          var filter = filtered && linqVisitor.Error == LinqElementCountErrorTypes.TooMany ? (LambdaExpression)linqVisitor.CauseOfLinqException.Arguments[1] : null;

          var items = GetItems(filter, linqVisitor.CauseOfLinqException, instanceOfMethodCallExpression);

          if (items != null)
          {
            message = $@"{message}

Value of {(filter != null ? linqVisitor.CauseOfLinqException : instanceOfMethodCallExpression)}: {Serializer.Serialize(items)}";
          }
        }

        return new HandledException(message, linqVisitor.CauseOfLinqException);
      }

      return null;
    }

    private List<object>? GetItems(LambdaExpression? filterExpression, MethodCallExpression causeOfException, Expression instanceOfMethodCallExpression)
    {
      var instance = ExpressionHelper.EvaluateExpression(instanceOfMethodCallExpression);

      if (!(instance is IEnumerable enumerable))
      {
        return null;
      }

      var items = new List<object>();

      var filter = filterExpression?.Compile(true);

      foreach (var i in enumerable)
      {
        bool AddItem()
        {
          items.Add(i);
          if (items.Count > 10)
          {
            return false;
          }

          return true;
        }

        if (filter != null)
        {
          var result = (bool)filter.DynamicInvoke(i);
          if (result)
          {
            if (!AddItem())
            {
              break;
            }
          }
        }
        else
        {
          if (!AddItem())
          {
            break;
          }
        }
      }

      return items;
    }

    private static string GetMethod(MethodCallExpression methodCallExpression)
    {
      if (methodCallExpression.Arguments.Count >= 2 && methodCallExpression.Arguments[1] is LambdaExpression)
      {
        return MethodCallToString(methodCallExpression);
      }

      return methodCallExpression.Method.Name;
    }

    private static FormattableString GetReasonMessage(MethodCallExpression methodCallExpression,
      LinqElementCountErrorTypes? error,
      int? actualCount, bool filtered, Expression instanceOfMethodCall)
    {
      return error switch
      {
        LinqElementCountErrorTypes.TooFew =>
        $"InvalidOperationException caused by calling {GetMethod(methodCallExpression)} on {instanceOfMethodCall} which contains no elements{(filtered ? " that match the filter" : "")}.",
        LinqElementCountErrorTypes.TooMany =>
        $"InvalidOperationException caused by calling {GetMethod(methodCallExpression)} on {instanceOfMethodCall} which contains more than one element{(filtered ? " that matches the filter" : "")}. Actual element count: {actualCount}.",
        _ => $"InvalidOperationException caused by calling {GetMethod(methodCallExpression)} on {instanceOfMethodCall}."
      };
    }

    private enum LinqElementCountErrorTypes
    {
      TooFew,
      TooMany
    }

    private class LinqElementCountVisitor : ExpressionVisitor
    {
      private readonly HashSet<string> _methodNames = new HashSet<string>()
      {
        nameof(Enumerable.Single),
        nameof(Enumerable.SingleOrDefault),
        nameof(Enumerable.First),
        nameof(Enumerable.FirstOrDefault)
      };

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        var result = base.VisitMethodCall(node);
        
        if (CauseOfLinqException != null)
        {
          return result;
        }
        
        if (_methodNames.Contains(node.Method.Name))
        {
          try
          {
            Expression.Lambda(node).Compile(true).DynamicInvoke();
          }
          catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
          {
            HandleException(node);
          }
        }

        return result;
      }

      public MethodCallExpression? CauseOfLinqException { get; private set; }
      public int? ActualCount { get; private set; }
      public LinqElementCountErrorTypes? Error { get; private set; }

      private void HandleException(MethodCallExpression node)
      {
        var instanceExpression = ExpressionHelper.GetInstanceOfMethodCall(node);

        var count = ExpressionHelper.GetCollectionItemCount(instanceExpression, node);

        if (count == 0 && (node.Method.Name == nameof(Enumerable.Single)
                           || node.Method.Name == nameof(Enumerable.First)))
        {
          CauseOfLinqException = node;
          ActualCount = count;
          Error = LinqElementCountErrorTypes.TooFew;
        }
        else if (count > 1 && (node.Method.Name == nameof(Enumerable.Single)
                               || node.Method.Name == nameof(Enumerable.SingleOrDefault)))
        {
          CauseOfLinqException = node;
          ActualCount = count;
          Error = LinqElementCountErrorTypes.TooMany;
        }
      }
    }
  }
}