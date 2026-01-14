using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Config;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

namespace Assertive.ExceptionPatterns
{
  internal class NullReferencePattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is NullReferenceException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var nullVisitor = new NullReferenceVisitor();

      nullVisitor.Visit(assertion.Expression);

      if (nullVisitor.CauseOfNullReference != null)
      {
        var message = nullVisitor.CauseOfNullReference switch
        {
          MemberExpression memberExpression => nullVisitor.ExceptionWasThrownInternally
            ? GetReasonMessageInternalException(memberExpression)
            : GetReasonMessage(memberExpression),
          MethodCallExpression methodCallExpression => nullVisitor.ExceptionWasThrownInternally
            ? GetReasonMessageInternalException(methodCallExpression)
            : GetReasonMessage(methodCallExpression),
          UnaryExpression { NodeType: ExpressionType.ArrayLength } unaryExpression =>
          GetReasonMessage(unaryExpression),
          BinaryExpression { NodeType: ExpressionType.ArrayIndex } binaryExpression =>
          GetReasonMessage(binaryExpression),
          _ => null
        };

        if (message != null)
        {
          // Append lambda item context if available
          if (nullVisitor.LambdaItemIndex.HasValue)
          {
            var serializedItem = Serializer.Serialize(nullVisitor.LambdaItem);
            message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{nullVisitor.LambdaItemIndex}] of {nullVisitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
          }

          return new HandledException(message, nullVisitor.CauseOfNullReference);
        }
      }

      return null;
    }

    private static FormattableString GetReasonMessageInternalException(MemberExpression expression)
    {
      return $"NullReferenceException was thrown inside {Configuration.Colors.Expression(expression.Member.Name)} on {expression}.";
    }

    private static FormattableString GetReasonMessageInternalException(MethodCallExpression expression)
    {
      return $"NullReferenceException was thrown inside {Configuration.Colors.Expression(expression.Method.Name)} on {expression}.";
    }

    private static FormattableString GetReasonMessage(UnaryExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing array length on {causeOfNullReference.Operand} which was null.";
    }
    
    private static FormattableString GetReasonMessage(BinaryExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing array index {causeOfNullReference.Right} on {causeOfNullReference.Left} which was null.";
    }
    
    private static FormattableString GetReasonMessage(MemberExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing {Configuration.Colors.Expression(causeOfNullReference.Member.Name)} on {causeOfNullReference.Expression} which was null.";
    }

    private static FormattableString GetReasonMessage(MethodCallExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by calling {Configuration.Colors.Expression(causeOfNullReference.Method.Name)} on {causeOfNullReference.Object} which was null.";
    }

    private class NullReferenceVisitor : LambdaAwareExpressionVisitor
    {
      public Expression? CauseOfNullReference { get; private set; }
      public bool ExceptionWasThrownInternally { get; private set; }

      protected override bool HasFoundResult => CauseOfNullReference != null;

      protected override Expression VisitUnary(UnaryExpression node)
      {
        var result = base.VisitUnary(node);

        if (node.NodeType != ExpressionType.ArrayLength)
        {
          return result;
        }

        if (CauseOfNullReference != null)
        {
          return result;
        }

        var (value, threwInternally) = EvaluateExpression(node.Operand);

        if (value == null)
        {
          CauseOfNullReference = node;
        }

        if (threwInternally)
        {
          ExceptionWasThrownInternally = true;
          CauseOfNullReference = node.Operand;
        }

        return result;
      }

      protected override Expression VisitBinary(BinaryExpression node)
      {
        var result = base.VisitBinary(node);

        if (node.NodeType != ExpressionType.ArrayIndex)
        {
          return result;
        }

        if (CauseOfNullReference != null)
        {
          return result;
        }

        var (value, threwInternally) = EvaluateExpression(node.Left);

        if (value == null)
        {
          CauseOfNullReference = node;
        }

        if (threwInternally)
        {
          ExceptionWasThrownInternally = true;
          CauseOfNullReference = node.Left;
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

        if (CauseOfNullReference != null)
        {
          return result;
        }

        // Static method cannot be cause of NullReference
        if (node.Object == null)
        {
          return result;
        }

        var (value, threwInternally) = EvaluateExpression(node.Object);

        if (value == null)
        {
          CauseOfNullReference = node;
        }

        if (threwInternally)
        {
          ExceptionWasThrownInternally = true;
          CauseOfNullReference = node.Object;
        }

        return result;
      }

      private (object? value, bool threwInternally) EvaluateExpression(Expression node)
      {
        try
        {
          var value = EvaluateExpressionWithBindings(node);
          return (value, false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
          return (null, true);
        }
        catch (InvalidOperationException)
        {
          // Expression contains unbound parameters that we don't have bindings for
          // Return a non-null sentinel so this isn't treated as the cause
          return (new object(), false);
        }
      }

      protected override Expression VisitMember(MemberExpression node)
      {
        var result = base.VisitMember(node);

        if (CauseOfNullReference != null)
        {
          return result;
        }

        var (value, threwInternally) = node.Expression != null ? EvaluateExpression(node.Expression) : (null, false);

        if (value == null)
        {
          if (node.Member is PropertyInfo p
              && p.GetGetMethod() != null
              && !p.GetGetMethod()!.IsStatic)
          {
            CauseOfNullReference = node;
          }
          else if (node.Member is FieldInfo { IsStatic: false })
          {
            CauseOfNullReference = node;
          }
        }

        if (threwInternally)
        {
          ExceptionWasThrownInternally = true;
          CauseOfNullReference = node.Expression;
        }

        return result;
      }
    }
  }
}