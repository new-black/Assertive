using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class DivideByZeroExceptionPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is DivideByZeroException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var visitor = new DivideByZeroVisitor();

      visitor.Visit(assertion.Expression);

      if (visitor.CauseOfDivideByZero == null)
      {
        return null;
      }

      var binaryExpression = visitor.CauseOfDivideByZero;
      var leftOperand = binaryExpression.Left;
      var rightOperand = binaryExpression.Right;

      var operation = binaryExpression.NodeType == ExpressionType.Divide ? "dividing" : "modulo";
      var operationSymbol = binaryExpression.NodeType == ExpressionType.Divide ? "/" : "%";

      // Get the right operand (divisor) info
      var divisorString = ExpressionHelper.IsConstantExpression(rightOperand)
        ? "0"
        : $"{ExpressionHelper.ExpressionToString(rightOperand, allowQuotation: false)} (value: 0)";

      var leftString = ExpressionHelper.ExpressionToString(leftOperand, allowQuotation: false);

      FormattableString message = (FormattableString)$"DivideByZeroException caused by {operation} {leftString} by {divisorString}.";

      // Append lambda item context if available
      if (visitor.LambdaItemIndex.HasValue)
      {
        var serializedItem = Serializer.Serialize(visitor.LambdaItem);
        message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{visitor.LambdaItemIndex}] of {visitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
      }

      return new HandledException(message, binaryExpression);
    }

    private class DivideByZeroVisitor : LambdaAwareExpressionVisitor
    {
      public BinaryExpression? CauseOfDivideByZero { get; private set; }

      protected override bool HasFoundResult => CauseOfDivideByZero != null;

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        return base.VisitMethodCall(node);
      }

      protected override Expression VisitBinary(BinaryExpression node)
      {
        var result = base.VisitBinary(node);

        if (CauseOfDivideByZero != null)
        {
          return result;
        }

        // Check for divide or modulo operations
        if (node.NodeType is ExpressionType.Divide or ExpressionType.Modulo)
        {
          if (ThrowsDivideByZeroException(node))
          {
            CauseOfDivideByZero = node;
          }
        }

        return result;
      }

      private bool ThrowsDivideByZeroException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is DivideByZeroException)
        {
          return true;
        }
        catch (DivideByZeroException)
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
