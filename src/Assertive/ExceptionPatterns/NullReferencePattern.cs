using System;
using System.Linq.Expressions;
using System.Reflection;
using static Assertive.ExpressionStringBuilder;

namespace Assertive.ExceptionPatterns
{
  internal class NullReferencePattern
  {
    public FailedAssertion Handle(Assertion assertion)
    {
      var nullVisitor = new NullReferenceVisitor();

      nullVisitor.Visit(assertion.Expression);

      if (nullVisitor.CauseOfNullReference != null)
      {
        string message = null;

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

        if (message != null)
        {
          return new FailedAssertion(assertion,
            $@"{message}

Assertion: {ExpressionToString(assertion.Expression)}"
,
            null);
        }
      }

      return null;
    }

    private string GetReasonMessageInternalException(MemberExpression expression)
    {
      return $"NullReferenceException was thrown inside {expression.Member.Name} on {ExpressionToString(expression)}.";
    }

    private string GetReasonMessageInternalException(MethodCallExpression expression)
    {
      return $"NullReferenceException was thrown inside {expression.Method.Name} on {ExpressionToString(expression)}.";
    }

    private string GetReasonMessage(MemberExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by accessing {causeOfNullReference.Member.Name} on {ExpressionToString(causeOfNullReference.Expression)} which was null.";
    }

    private string GetReasonMessage(MethodCallExpression causeOfNullReference)
    {
      return
        $"NullReferenceException caused by calling {causeOfNullReference.Method.Name} on {ExpressionToString(causeOfNullReference.Object)} which was null.";
    }

    private class NullReferenceVisitor : ExpressionVisitor
    {
      public Expression CauseOfNullReference { get; set; }
      public bool ExceptionWasThrownInternally { get; set; }

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

      private static (object value, bool threwInternally) EvaluateExpression(Expression node)
      {
        try
        {
          var value = Expression.Lambda(node).Compile(true).DynamicInvoke();

          return (value, false);
        }
        catch (TargetInvocationException ex) when(ex.InnerException is NullReferenceException) 
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