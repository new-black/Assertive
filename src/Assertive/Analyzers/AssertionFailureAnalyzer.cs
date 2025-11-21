using System.Collections.Generic;
using Assertive.Interfaces;

namespace Assertive.Analyzers
{
  internal class AssertionFailureAnalyzer
  {
    private readonly AssertionFailureContext _context;

    public AssertionFailureAnalyzer(AssertionFailureContext context)
    {
      _context = context;
    }

    public List<FailedAnalyzedAssertion> AnalyzeAssertionFailures()
    {
      var treeProvider = new AssertionTreeProvider(_context.Assertion.Expression);

      var tree = treeProvider.GetTree();
      
      var executor = new AssertionTreeExecutor(tree, _context.AssertionException);

      var failedParts = executor.Execute();
      
      var failedAssertions = new List<FailedAnalyzedAssertion>(failedParts.Length);

      foreach (var part in failedParts)
      {
        try
        {
          if (part.Exception == null)
          {
            var friendlyMessageProvider = new FriendlyMessageProvider(_context, part);

            var friendlyMessage = friendlyMessageProvider.TryGetFriendlyMessage();

            var failedAssertion = new FailedAnalyzedAssertion(part, friendlyMessage?.Message, friendlyMessage?.Pattern, friendlyMessage?.ExpectedAndActual);

            failedAssertions.Add(failedAssertion);
          }
          else
          {
            failedAssertions.Add(new FriendlyMessageProviderForException(_context).AnalyzeException(part));
          }
        }
        catch
        {
          failedAssertions.Add(new FailedAnalyzedAssertion(part, null, null, default(ExpectedAndActual?)));
        }
      }

      return failedAssertions;
    }
  }
}