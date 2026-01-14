using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class InvalidCastExceptionPattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is InvalidCastException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var visitor = new InvalidCastVisitor();

      visitor.Visit(assertion.Expression);

      if (visitor.CauseOfInvalidCast == null)
      {
        return null;
      }

      var castExpression = visitor.CauseOfInvalidCast;
      var operand = castExpression.Operand;
      var targetType = castExpression.Type;

      // Get the actual value and its type
      object? actualValue = null;
      Type? actualType = null;
      try
      {
        actualValue = ExpressionHelper.EvaluateExpression(visitor.ReplaceParametersWithBindings(operand));
        actualType = actualValue?.GetType();
      }
      catch
      {
        // Could not evaluate
      }

      var operandString = ExpressionHelper.ExpressionToString(operand, allowQuotation: false);
      var targetTypeName = TypeHelper.TypeNameToString(targetType);
      var actualTypeName = actualType != null ? TypeHelper.TypeNameToString(actualType) : "unknown";

      FormattableString message = actualType != null
        ? (FormattableString)$"InvalidCastException caused by casting {operandString} to {targetTypeName}. Actual type was {actualTypeName}."
        : (FormattableString)$"InvalidCastException caused by casting {operandString} to {targetTypeName}.";

      // Append lambda item context if available
      if (visitor.LambdaItemIndex.HasValue)
      {
        var serializedItem = Serializer.Serialize(visitor.LambdaItem);
        message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{visitor.LambdaItemIndex}] of {visitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
      }

      return new HandledException(message, castExpression);
    }

    private class InvalidCastVisitor : LambdaAwareExpressionVisitor
    {
      public UnaryExpression? CauseOfInvalidCast { get; private set; }

      protected override bool HasFoundResult => CauseOfInvalidCast != null;

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        return base.VisitMethodCall(node);
      }

      protected override Expression VisitUnary(UnaryExpression node)
      {
        var result = base.VisitUnary(node);

        if (CauseOfInvalidCast != null)
        {
          return result;
        }

        // Check for explicit cast (Convert or ConvertChecked)
        if (node.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked && ThrowsInvalidCastException(node))
        {
          CauseOfInvalidCast = node;
        }

        return result;
      }

      private bool ThrowsInvalidCastException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidCastException)
        {
          return true;
        }
        catch (InvalidCastException)
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
