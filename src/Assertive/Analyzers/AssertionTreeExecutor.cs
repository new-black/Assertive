using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Assertive.Expressions;

namespace Assertive.Analyzers
{
  internal class AssertionTreeExecutor
  {
    private readonly AssertionNode _root;
    private readonly Exception? _assertionException;

    public AssertionTreeExecutor(AssertionNode root, Exception? assertionException)
    {
      _root = root;
      _assertionException = assertionException;
    }

    public FailedAssertion[] Execute()
    {
      if (_root.Type == AssertionNodeType.Leaf)
      {
        _failedAssertions.Add(new FailedAssertion(_root.Expression, _assertionException));
      }
      else
      {
        ExecuteNode(_root);
      }

      return _failedAssertions.ToArray();
    }

    private readonly List<FailedAssertion> _failedAssertions = new List<FailedAssertion>();

    private bool TestAssertion(Expression assertion)
    {
      var lambda = Expression.Lambda<Func<bool>>(assertion);

      var compiled = lambda.Compile(ExpressionHelper.ShouldUseInterpreter(lambda));

      try
      {
        var success = compiled();

        if (!success)
        {
          _failedAssertions.Add(new FailedAssertion(assertion, null));
        }

        return success;
      }
      catch (Exception ex)
      {
        _failedAssertions.Add(new FailedAssertion(assertion, ex));

        return false;
      }
    }

    private bool ExecuteNode(AssertionNode node)
    {
      return node.Type switch
      {
        AssertionNodeType.Leaf => TestAssertion(node.Expression),
        AssertionNodeType.And => ExecuteAnd(node),
        AssertionNodeType.AndAlso => ExecuteAndAlso(node),
        AssertionNodeType.Or => ExecuteOr(node),
        AssertionNodeType.OrElse => ExecuteOrElse(node),
        _ => throw new InvalidOperationException()
      };
    }

    private bool ExecuteAndAlso(AssertionNode node)
    {
      return ExecuteNode(node.Left!) && ExecuteNode(node.Right!);
    }

    private bool ExecuteAnd(AssertionNode node)
    {
      return ExecuteNode(node.Left!) & ExecuteNode(node.Right!);
    }

    private bool ExecuteOr(AssertionNode node)
    {
      return ExecuteNode(node.Left!) | ExecuteNode(node.Right!);
    }

    private bool ExecuteOrElse(AssertionNode node)
    {
      return ExecuteNode(node.Left!) || ExecuteNode(node.Right!);
    }
  }
}