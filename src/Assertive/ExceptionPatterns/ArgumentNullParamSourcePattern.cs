using System;
using System.Linq.Expressions;
using System.Reflection;
using Assertive.Analyzers;
using Assertive.Expressions;
using Assertive.Helpers;
using Assertive.Interfaces;

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
        FormattableString message = GetReasonMessage(nullVisitor.CauseOfArgumentNull);

        // Append lambda item context if available
        if (nullVisitor.LambdaItemIndex.HasValue)
        {
          var serializedItem = Serializer.Serialize(nullVisitor.LambdaItem);
          message = $"{message}{Environment.NewLine}{Environment.NewLine}On item [{nullVisitor.LambdaItemIndex}] of {nullVisitor.CollectionExpression}:{Environment.NewLine}{serializedItem}";
        }

        return new HandledException(message, nullVisitor.CauseOfArgumentNull);
      }

      return null;
    }

    private static FormattableString GetReasonMessage(MethodCallExpression causeOfNullReference)
    {
      return
        $"ArgumentNullException caused by calling {StaticRenderMethodCallExpression.Wrap(causeOfNullReference)} on {ExpressionHelper.GetInstanceOfMethodCall(causeOfNullReference)} which was null.";
    }

    private class ArgumentNullVisitor : LambdaAwareExpressionVisitor
    {
      public MethodCallExpression? CauseOfArgumentNull { get; private set; }

      protected override bool HasFoundResult => CauseOfArgumentNull != null;

      protected override Expression VisitMethodCall(MethodCallExpression node)
      {
        if (TryVisitLambdaMethodCall(node))
        {
          return node;
        }

        var result = base.VisitMethodCall(node);

        if (CauseOfArgumentNull != null)
        {
          return result;
        }

        // Only interested in static methods (extension methods)
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

      private bool ThrowsArgumentNullException(Expression node)
      {
        try
        {
          EvaluateExpressionWithBindings(node);
          return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException { ParamName: "source" })
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