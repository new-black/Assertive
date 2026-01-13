using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class IndexOutOfRangeExceptionPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is IndexOutOfRangeException or ArgumentOutOfRangeException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var visitor = new IndexOutOfRangeExceptionVisitor();

      visitor.Visit(assertion.Expression);

      var causeOfException = visitor.CauseOfIndexOutOfRangeException;

      if (causeOfException == null)
      {
        return null;
      }

      Expression indexExpression;
      Expression? operand;
      int? actualLength;
      string lengthString;

      if (causeOfException.NodeType == ExpressionType.ArrayIndex && assertion.Exception is IndexOutOfRangeException)
      {
        var b = (BinaryExpression)causeOfException;

        indexExpression = b.Right;
        operand = b.Left;
        actualLength = (int?)ExpressionHelper.EvaluateExpression(Expression.ArrayLength(visitor.ReplaceParametersWithBindings(operand)));
        lengthString = "length";
      }
      else if (assertion.Exception is ArgumentOutOfRangeException &&
               causeOfException is MethodCallExpression { Method.Name: "get_Item", Arguments.Count: 1 } methodCallExpression)
      {
        indexExpression = methodCallExpression.Arguments[0];
        operand = methodCallExpression.Object;
        actualLength = operand != null ? ExpressionHelper.GetCollectionItemCount(visitor.ReplaceParametersWithBindings(operand)) : null;
        lengthString = "count";
      }
      else
      {
        return null;
      }

      var indexExpressionString = ExpressionHelper.IsConstantExpression(indexExpression)
        ? $"{indexExpression}"
        : (FormattableString)$"{indexExpression} (value: {indexExpression.ToValue()})";

      FormattableString message =
        $"{assertion.Exception.GetType().Name} caused by accessing index {indexExpressionString} on {operand}, actual {lengthString} was {actualLength?.ToString() ?? "unknown"}.";

      // Append lambda item context if available
      if (visitor.LambdaItemIndex.HasValue)
      {
        var serializedItem = Serializer.Serialize(visitor.LambdaItem);
        message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{visitor.LambdaItemIndex}] of {visitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
      }

      return new HandledException(message, causeOfException);
    }

    private class IndexOutOfRangeExceptionVisitor : LambdaAwareExpressionVisitor
    {
      public Expression? CauseOfIndexOutOfRangeException { get; private set; }

      protected override bool HasFoundResult => CauseOfIndexOutOfRangeException != null;

      protected override Expression VisitBinary(BinaryExpression node)
      {
        if (node.NodeType != ExpressionType.ArrayIndex)
        {
          return base.VisitBinary(node);
        }

        var result = base.VisitBinary(node);

        if (CauseOfIndexOutOfRangeException == null && ThrowsIndexOutOfRangeException(node))
        {
          CauseOfIndexOutOfRangeException = node;
        }

        return result;
      }

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        var result = base.VisitMethodCall(node);

        if (node.Method.Name != "get_Item")
        {
          return result;
        }

        if (CauseOfIndexOutOfRangeException == null && ThrowsIndexOutOfRangeException(node))
        {
          CauseOfIndexOutOfRangeException = node;
        }

        return result;
      }

      private bool ThrowsIndexOutOfRangeException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
          return true;
        }
        catch (InvalidOperationException)
        {
          // Unbound parameters - can't evaluate
          return false;
        }
      }
    }
  }
}