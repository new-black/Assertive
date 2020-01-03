using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Assertive
{
  internal class AssertionPartProvider : ExpressionVisitor
  {
    private readonly Expression<Func<bool>> _assertion;
    private readonly List<Assertion> _failedAssertions;
    
    private bool _binaryExpressionsEncountered = false;

    public AssertionPartProvider(Expression<Func<bool>> assertion)
    {
      _assertion = assertion;
      _failedAssertions = new List<Assertion>();
    }

    internal Assertion[] GetFailedAssertions()
    {
      this.Visit(_assertion.Body);

      if (_failedAssertions.Count == 0 && !_binaryExpressionsEncountered)
      {
        _failedAssertions.Add(new Assertion(_assertion.Body, null));
      }
      
      return _failedAssertions.ToArray();
    }

    private bool TestAssertion(Expression expression)
    {
      var lambda = Expression.Lambda<Func<bool>>(expression);

      var compiled = lambda.Compile();

      try
      {
        var success = compiled();

        if (!success)
        {
          _failedAssertions.Add(new Assertion(expression, null));
        }

        return success;
      }
      catch (Exception ex)
      {
        _failedAssertions.Add(new Assertion(expression, ex));

        return false;
      }
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
      return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
      if (node.Type == typeof(bool))
      {
        ProcessBinaryExpression(node);
      }

      return node;
    }

    private bool ProcessBinaryExpression(Expression node)
    {
      if (node is BinaryExpression binaryExpression)
      {
        switch (binaryExpression.NodeType)
        {
          case ExpressionType.And: return ProcessAnd(binaryExpression);
          case ExpressionType.AndAlso: return ProcessAndAlso(binaryExpression);
          case ExpressionType.Or: return ProcessOr(binaryExpression);
          case ExpressionType.OrElse: return ProcessOrElse(binaryExpression);
          default:

            var passed = TestAssertion(binaryExpression);

            return passed;
        }
      }
      else
      {
        var passed = TestAssertion(node);

        return passed;
      }
    }

    private bool ProcessOr(BinaryExpression binaryExpression)
    {
      _binaryExpressionsEncountered = true;

      var leftPassed = ProcessBinaryExpression(binaryExpression.Left);
      var rightPassed = ProcessBinaryExpression(binaryExpression.Right);

      if (!leftPassed && !rightPassed)
      {
        return false;
      }

      return true;
    }

    private bool ProcessOrElse(BinaryExpression binaryExpression)
    {
      _binaryExpressionsEncountered = true;

      var leftPassed = ProcessBinaryExpression(binaryExpression.Left);

      if (!leftPassed)
      {
        var rightPassed = ProcessBinaryExpression(binaryExpression.Right);

        if (!rightPassed)
        {
          return false;
        }
      }

      return true;
    }

    private bool ProcessAnd(BinaryExpression binaryExpression)
    {
      _binaryExpressionsEncountered = true;

      var leftPassed = ProcessBinaryExpression(binaryExpression.Left);

      var rightPassed = ProcessBinaryExpression(binaryExpression.Right);

      if (!leftPassed || !rightPassed)
      {
        return false;
      }

      return true;
    }

    private bool ProcessAndAlso(BinaryExpression binaryExpression)
    {
      _binaryExpressionsEncountered = true;

      var leftPassed = ProcessBinaryExpression(binaryExpression.Left);

      if (!leftPassed)
      {
        return false;
      }

      var rightPassed = ProcessBinaryExpression(binaryExpression.Right);

      if (!rightPassed)
      {
        return false;
      }

      return true;
    }
  }
}
