using System;
using System.Linq.Expressions;

namespace Assertive.Test
{
  public abstract class AssertionTestBase
  {
    protected void ShouldThrow(Expression<Func<object>> assertion, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.Throws(assertion);
      }
      catch (Exception ex)
      {
        throws = true;
        Xunit.Assert.StartsWith(expectedMessage, ex.Message);
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldThrow<T>(Expression<Func<object>> assertion, string expectedMessage) where T : Exception
    {
      bool throws = false;

      try
      {
        Assert.Throws<T>(assertion);
      }
      catch (Exception ex)
      {
        throws = true;
        Xunit.Assert.StartsWith(expectedMessage, ex.Message);
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldFail(Expression<Func<bool>> assertion, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.That(assertion);
      }
      catch (Exception ex)
      {
        throws = true;
        Xunit.Assert.StartsWith(expectedMessage, ex.Message);
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldFail(Expression<Func<bool>> assertion, Expression<Func<object>> context, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.That(assertion, context);
      }
      catch (Exception ex)
      {
        throws = true;
        Xunit.Assert.Contains(expectedMessage, ex.Message);
      }

      Xunit.Assert.True(throws);
    }

    protected void ShouldFail(Expression<Func<bool>> assertion, object message, string expectedMessage)
    {
      bool throws = false;

      try
      {
        Assert.That(assertion, message);
      }
      catch (Exception ex)
      {
        throws = true;
        Xunit.Assert.Contains(expectedMessage, ex.Message);
      }

      Xunit.Assert.True(throws);
    }
//    protected void ShouldFail(Expression<Func<object>> assertion, string expectedMessage)
//    {
//      bool throws = false;
//
//      try
//      {
//        Assert.That(assertion);
//      }
//      catch (Exception ex)
//      {
//        throws = true;
//        Xunit.Assert.StartsWith(expectedMessage, ex.Message);
//      }
//
//      Xunit.Assert.True(throws);
//    } 
  }
}