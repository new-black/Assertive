using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class IndexOutOfRangeExceptionPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is IndexOutOfRangeException
                                                || exception is ArgumentOutOfRangeException;

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
      Expression operand;
      int? actualLength;
      string lengthString;

      if (causeOfException.NodeType == ExpressionType.ArrayIndex && assertion.Exception is IndexOutOfRangeException)
      {
        var b = (BinaryExpression)causeOfException;

        indexExpression = b.Right;
        operand = b.Left;
        actualLength = (int?)ExpressionHelper.EvaluateExpression(Expression.ArrayLength(operand));
        lengthString = "length";
      }
      else if (assertion.Exception is ArgumentOutOfRangeException &&
               causeOfException is MethodCallExpression methodCallExpression &&
               methodCallExpression.Method.Name == "get_Item"
               && methodCallExpression.Arguments.Count == 1)
      {
        indexExpression = methodCallExpression.Arguments[0];
        operand = methodCallExpression.Object;
        actualLength = ExpressionHelper.GetCollectionItemCount(operand);
        lengthString = "count";
      }
      else
      {
        return null;
      }

      var indexExpressionString = ExpressionHelper.IsConstantExpression(indexExpression) ? 
        $"{indexExpression}" 
        : (FormattableString)$"{indexExpression} (value: {indexExpression.ToValue()})";

      FormattableString message = 
        $"{assertion.Exception.GetType().Name} caused by accessing index {indexExpressionString} on {operand}, actual {lengthString} was {actualLength?.ToString() ?? "unknown"}.";

      return new HandledException(message, causeOfException);
    }

    private class IndexOutOfRangeExceptionVisitor : ExpressionVisitor
    {
      public Expression? CauseOfIndexOutOfRangeException { get; private set; }
      
      protected override Expression VisitBinary(BinaryExpression node)
      {
        if (node.NodeType != ExpressionType.ArrayIndex)
        {
          return base.VisitBinary(node);
        }

        var result = base.VisitBinary(node);

        if (ThrowsIndexOufOfRangeException(result))
        {
          CauseOfIndexOutOfRangeException = node;
        }

        return result;
      }

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        var result = base.VisitMethodCall(node);

        if (node.Method.Name != "get_Item")
        {
          return result;
        }
        
        if (ThrowsIndexOufOfRangeException(result))
        {
          CauseOfIndexOutOfRangeException = node;
        }

        return result;
      }

      private static bool ThrowsIndexOufOfRangeException(Expression node)
      {
        try
        {
          Expression.Lambda(node).Compile(true).DynamicInvoke();

          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is IndexOutOfRangeException 
                                                   || ex.InnerException is ArgumentOutOfRangeException)
        {
          return true;
        }
      }
    }
  }
}