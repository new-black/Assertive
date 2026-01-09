using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Assertive.Analyzers;
using Assertive.Helpers;
using Assertive.Expressions;
using static Assertive.Expressions.ExpressionHelper;

namespace Assertive
{
  internal partial class AssertImpl
  {
    internal readonly struct ThrowsResult
    {
      public ThrowsResult(Exception? failure, Exception? thrown)
      {
        Failure = failure;
        Thrown = thrown;
      }

      public Exception? Failure { get; }
      public Exception? Thrown { get; }
    }

    public static Exception? That(Expression<Func<bool>> assertion, object? message, Expression<Func<object>>? context)
    {
      var compiledAssertion = assertion.Compile(ShouldUseInterpreter(assertion));

      Exception? exceptionToThrow = null;

      try
      {
        var result = compiledAssertion();

        if (!result)
        {
          var exceptionProvider = new FailedAssertionExceptionProvider(new Assertion(assertion, message, context));

          exceptionToThrow = exceptionProvider.GetException();
        }
      }
      catch (Exception ex)
      {
        var exceptionProvider = new FailedAssertionExceptionProvider(new Assertion(assertion, message, context));

        exceptionToThrow = exceptionProvider.GetException(ex);
      }

      return exceptionToThrow;
    }

    public static async Task<ThrowsResult> Throws(Expression<Func<Task>> expression,
      Type? expectedExceptionType = null, LambdaExpression? exceptionAssertion = null)
    {
      var threw = false;

      Exception? thrownException = null;
      var bodyExpression = GetBodyExpression(expression);

      try
      {
        var task = (Task)expression.Compile(ShouldUseInterpreter(expression)).DynamicInvoke()!;

        await task;
      }
      catch (Exception ex)
      {
        thrownException = ex;
        threw = true;

        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex))
        {
          return new ThrowsResult(ExceptionHelper.GetException(
            $"Expected {ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.GetType().FullName} instead."), thrownException);
        }
      }

      if (!threw)
      {
        return new ThrowsResult(ExceptionHelper.GetException($"Expected {ExpressionToString(bodyExpression)} to throw an exception, but it did not."), null);
      }

      var assertionFailure = EvaluateExceptionAssertion(exceptionAssertion, thrownException!);

      return new ThrowsResult(assertionFailure, thrownException);
    }

    public static ThrowsResult Throws(LambdaExpression expression,
      Type? expectedExceptionType = null, LambdaExpression? exceptionAssertion = null)
    {
      var threw = false;
      Exception? thrownException = null;

      var bodyExpression = GetBodyExpression(expression);

      try
      {
        expression.Compile(ShouldUseInterpreter(expression)).DynamicInvoke();
      }
      catch (TargetInvocationException ex)
      {
        thrownException = ex.InnerException;
        if (expectedExceptionType != null && !expectedExceptionType.IsInstanceOfType(ex.InnerException))
        {
          return new ThrowsResult(ExceptionHelper.GetException(
            $"Expected {ExpressionToString(bodyExpression)} to throw an exception of type {expectedExceptionType.FullName}, but it threw an exception of type {ex.InnerException!.GetType().FullName} instead."), thrownException);
        }

        threw = true;
      }

      if (!threw)
      {
        return new ThrowsResult(ExceptionHelper.GetException($"Expected {ExpressionToString(bodyExpression)} to throw an exception, but it did not."), null);
      }

      var assertionFailure = EvaluateExceptionAssertion(exceptionAssertion, thrownException!);

      return new ThrowsResult(assertionFailure, thrownException);
    }

    private static Expression GetBodyExpression(LambdaExpression expression)
    {
      Expression bodyExpression;

      if (expression.Body.NodeType == ExpressionType.Convert
          && expression.Body is UnaryExpression convertExpression
          && expression.Body.Type == typeof(object))
      {
        bodyExpression = convertExpression.Operand;
      }
      else
      {
        bodyExpression = expression.Body;
      }

      return bodyExpression;
    }

    private static Exception? EvaluateExceptionAssertion(LambdaExpression? exceptionAssertion, Exception exception)
    {
      if (exceptionAssertion == null)
      {
        return null;
      }

      if (exceptionAssertion.Parameters.Count != 1)
      {
        throw new ArgumentException("Exception assertion must take exactly one parameter.");
      }

      var parameterType = exceptionAssertion.Parameters[0].Type;
      if (!parameterType.IsInstanceOfType(exception))
      {
        throw new ArgumentException($"Exception assertion parameter type {parameterType.FullName} is not assignable from thrown exception type {exception.GetType().FullName}.");
      }

      var replacedBody = new ParameterReplacer(exceptionAssertion.Parameters[0],
        new NamedConstantExpression(exceptionAssertion.Parameters[0].Name ?? "exception", exception))
        .Visit(exceptionAssertion.Body)!;

      var wrapper = Expression.Lambda<Func<bool>>(replacedBody);

      return That(wrapper, null, null);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
      private readonly ParameterExpression _parameter;
      private readonly Expression _replacement;

      public ParameterReplacer(ParameterExpression parameter, Expression replacement)
      {
        _parameter = parameter;
        _replacement = replacement;
      }

      protected override Expression VisitParameter(ParameterExpression node)
      {
        if (node == _parameter)
        {
          return _replacement;
        }

        return base.VisitParameter(node);
      }
    }
  }
}
