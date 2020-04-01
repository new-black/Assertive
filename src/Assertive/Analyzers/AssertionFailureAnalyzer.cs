using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Assertive.Analyzers
{
  internal class AssertionFailureAnalyzer
  {
    private readonly Expression<Func<bool>> _assertion;
    private readonly Exception? _assertionException;

    public AssertionFailureAnalyzer(Expression<Func<bool>> assertion, Exception? assertionException)
    {
      _assertion = assertion;
      _assertionException = assertionException;
    }

    public List<FailedAnalyzedAssertion> AnalyzeAssertionFailures()
    {
      var treeProvider = new AssertionTreeProvider(_assertion);

      var tree = treeProvider.GetTree();
      
      var executor = new AssertionTreeExecutor(tree, _assertionException);

      var failedParts = executor.Execute();
      
      var failedAssertions = new List<FailedAnalyzedAssertion>(failedParts.Length);

      foreach (var part in failedParts)
      {
        try
        {
          if (part.Exception == null)
          {
            var friendlyMessageProvider = new FriendlyMessageProvider(part);

            var friendlyMessage = friendlyMessageProvider.TryGetFriendlyMessage();

            var failedAssertion = new FailedAnalyzedAssertion(part, friendlyMessage?.Message, friendlyMessage?.Pattern);

            failedAssertions.Add(failedAssertion);
          }
          else
          {
            failedAssertions.Add(new FriendlyMessageProviderForException().AnalyzeException(part));
          }
        }
        catch
        {
          failedAssertions.Add(new FailedAnalyzedAssertion(part, null, null));
        }
      }

      return failedAssertions;
    }
  }
}