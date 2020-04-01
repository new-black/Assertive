using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Assertive.Analyzers;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class AssertionPartProviderTests
  {
    [Fact]
    public void Single_assertion_results_in_one_node()
    {
      var list = new List<int>();

      var tree = GetAssertionParts(() => list.Count == 0);
      
      Assert(() => tree.Type == AssertionNodeType.Leaf && tree.Left == null && tree.Right == null);
    }
    
    [Fact]
    public void AndAlso_assertion_results_in_three_nodes()
    {
      var list = new List<int>();

      var tree = GetAssertionParts(() => list.Count > 0 && list.Count < 10);
      
      Assert(() => tree.Type == AssertionNodeType.AndAlso 
                   && tree.Left.Type == AssertionNodeType.Leaf && tree.Right.Type == AssertionNodeType.Leaf);
    }
    
    [Fact]
    public void Double_AndAlso_assertion_results_in_five_nodes()
    {
      var list = new List<int>();

      var tree = GetAssertionParts(() => list.Count > 0 
                                         && list.Count < 10 
                                         && !list.Contains(1));
      
      Assert(() => tree.Type == AssertionNodeType.AndAlso
                   && tree.Left.Type == AssertionNodeType.AndAlso
                   && tree.Left.Left.Type == AssertionNodeType.Leaf
                   && tree.Left.Right.Type == AssertionNodeType.Leaf
                   && tree.Right.Type == AssertionNodeType.Leaf 
                   );
    }

    [Fact]
    public void And_works()
    {
      
    }
    
    private AssertionNode GetAssertionParts(Expression<Func<bool>> assertion)
    {
      return new AssertionTreeProvider(assertion).GetTree();
    }
  }
}