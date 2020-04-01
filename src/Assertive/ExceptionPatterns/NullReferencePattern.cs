using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
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
        FormattableString? message = null;

        if (nullVisitor.CauseOfNullReference is MemberExpression memberExpression)
        {
          message = nullVisitor.ExceptionWasThrownInternally
            ? GetReasonMessageInternalException(memberExpression)
            : GetReasonMessage(memberExpression);
        }
        else if (nullVisitor.CauseOfNullReference is MethodCallExpression methodCallExpression)
        {
          message = nullVisitor.ExceptionWasThrownInternally
            ? GetReasonMessageInternalException(methodCallExpression)
            : GetReasonMessage(methodCallExpression);
        }
        else if (nullVisitor.CauseOfNullReference is UnaryExpression unaryExpression &&
                 unaryExpression.NodeType == ExpressionType.ArrayLength)
        {
          message = GetReasonMessage(unaryExpression);
        }
        else if (nullVisitor.CauseOfNullReference is BinaryExpression binaryExpression
                 && binaryExpression.NodeType == ExpressionType.ArrayIndex)
        {
          message = GetReasonMessage(binaryExpression);
        }

        if (message != null)
        {
          return new HandledException(message, nullVisitor.CauseOfNullReference);
        }
      }

      return null;
    }

    private FormattableString GetReasonMessageInternalException(MemberExpression expression)
    {
      return $"NullReferenceException was thrown inside {expression.Member.Name} on {expression}.";
    }

    private FormattableString GetReasonMessageInternalException(MethodCallExpression expression)
    {
      return $"NullReferenceException was thrown inside {expression.Method.Name} on {expression}.";
    }

    private FormattableString GetReasonMessage(UnaryExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing array length on {causeOfNullReference.Operand} which was null.";
    }
    
    private FormattableString GetReasonMessage(BinaryExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing array index {causeOfNullReference.Right} on {causeOfNullReference.Left} which was null.";
    }
    
    private FormattableString GetReasonMessage(MemberExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing {causeOfNullReference.Member.Name} on {causeOfNullReference.Expression} which was null.";
    }

    private FormattableString GetReasonMessage(MethodCallExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by calling {causeOfNullReference.Method.Name} on {causeOfNullReference.Object} which was null.";
    }

    private class NullReferenceVisitor : ExpressionVisitor
    {
      public Expression? CauseOfNullReference { get; private set; }
      public bool ExceptionWasThrownInternally { get; private set; }

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

      private static (object? value, bool threwInternally) EvaluateExpression(Expression node)
      {
        try
        {
          var value = Expression.Lambda(node).Compile(true).DynamicInvoke();

          return (value, false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
        {
          return (null, true);
        }
      }

      protected override Expression VisitMember(MemberExpression node)
      {
        var result = base.VisitMember(node);

        if (CauseOfNullReference != null)
        {
          return result;
        }

        var (value, threwInternally) = EvaluateExpression(node.Expression);

        if (value == null)
        {
          if (node.Member is PropertyInfo p
              && p.GetGetMethod() != null
              && !p.GetGetMethod().IsStatic)
          {
            CauseOfNullReference = node;
          }
          else if (node.Member is FieldInfo f && !f.IsStatic)
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