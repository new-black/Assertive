using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Assertive.ExceptionPatterns
{
  internal class ArgumentNullParamSourcePattern : IExceptionHandlerPattern
  {
    public bool IsMatch(Exception exception) => exception is ArgumentNullException;

    public HandledException? Handle(FailedAssertion assertion)
    {
      var nullVisitor = new ArgumentNullVisitor();

      nullVisitor.Visit(assertion.Expression);

      if (nullVisitor.CauseOfArgumentNull != null)
      {
        var message = GetReasonMessage(nullVisitor.CauseOfArgumentNull);

        return new HandledException(message, nullVisitor.CauseOfArgumentNull);
      }

      return null;
    }

    private FormattableString GetReasonMessage(MethodCallExpression causeOfNullReference)
    {
      return
        $"ArgumentNullException caused by calling {causeOfNullReference.Method.Name} on {ExpressionHelper.GetInstanceOfMethodCall(causeOfNullReference)} which was null.";
    }

    private class ArgumentNullVisitor : ExpressionVisitor
    {
      public MethodCallExpression? CauseOfArgumentNull { get; private set; }

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        var result = base.VisitMethodCall(node);

        if (CauseOfArgumentNull != null)
        {
          return result;
        }

        // Only interested in static methods
        if (node.Object != null)
        {
          return result;
        }

        if (ThrowsArgumentNullException(node))
        {
          CauseOfArgumentNull = node;
        }

        return result;
      }

      private static bool ThrowsArgumentNullException(Expression node)
      {
        try
        {
          Expression.Lambda(node).Compile(true).DynamicInvoke();

          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException argumentNullException
                                                   && argumentNullException.ParamName == "source")
        {
          return true;
        }
      }
    }
  }
}