using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  public class AssertionTreeExecutorTests
  {
    [Fact]
    public void Single_failing_assertion()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", false));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 1);
    }
    
    [Fact]
    public void Single_failing_assertion_is_only_executed_once()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", false));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 1);
    }
    
    [Fact]
    public void Single_throwing_assertion()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Throw("one"));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 1);
    }
    
    [Fact]
    public void And_also_with_throwing_assertion()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Throw("one") && a.Run("two", true));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 0);
    }
    
    [Fact]
    public void Single_successful_assertion()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", true));
      
      Assert(() => failedAssertions.Length == 0);
      Assert(() => a.ExecutionCount("one") == 1);
    }
    
    [Fact]
    public void AndAlso_both_failing()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", false) && a.Run("two", false));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 0);
    }
    
    [Fact]
    public void AndAlso_first_failing()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", false) && a.Run("two", true));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 0);
    }
    
    [Fact]
    public void AndAlso_second_failing()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", true) && a.Run("two", false));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 2);
    }
    
    [Fact]
    public void And_both_failing()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", false) & a.Run("two", false));
      
      Assert(() => failedAssertions.Length == 2);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 2);
    }
    
    [Fact]
    public void And_first_failing()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", false) & a.Run("two", true));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 2);
    }
    
    [Fact]
    public void And_second_failing()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", true) & a.Run("two", false));
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 2);
    }

    [Fact]
    public void Multiple_and_also_1()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", true) 
                                           && a.Run("two", false)
                                           && a.Run("three", false)
                                           );
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 2 && a.ExecutionCount("three") == 0);
    }
    
    [Fact]
    public void Multiple_and_also_2()
    {
      var a = new A();

      var failedAssertions = Execute(() => a.Run("one", true) 
                                           && a.Run("two", false)
                                           && a.Run("three", true)
      );
      
      Assert(() => failedAssertions.Length == 1);
      Assert(() => a.ExecutionCount("one") == 2 && a.ExecutionCount("two") == 2 && a.ExecutionCount("three") == 0);
    }

    private FailedAssertion[] Execute(Expression<Func<bool>> assertions)
    {
      Exception ex = null;
      bool success = false;
      
      try
      {
        success = assertions.Compile(true)();
      }
      catch(Exception e)
      {
        ex = e;
      }

      var tree = new AssertionTreeProvider(assertions).GetTree();

      if (!success)
      {
        return new AssertionTreeExecutor(tree, ex).Execute();
      }
      else
      {
        return Array.Empty<FailedAssertion>();
      }
    }

    private class A
    {
      private readonly Dictionary<string, int> _executions = new Dictionary<string, int>();
      
      public int ExecutionCount(string s)
      {
        _executions.TryGetValue(s, out var i);

        return i;
      }
      
      public bool Run(string s, bool b)
      {
        if (!_executions.ContainsKey(s))
        {
          _executions.Add(s, 0);
        }

        _executions[s]++;
        
        return b;
      }
      
      public bool Throw(string s)
      {
        if (!_executions.ContainsKey(s))
        {
          _executions.Add(s, 0);
        }

        _executions[s]++;
        
        throw new Exception();
      } 
    }
  }
}