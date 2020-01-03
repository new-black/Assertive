using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Assertive
{
  internal class FailedAssertionExceptionProvider
  {
    private readonly Expression<Func<bool>> _assertion;

    public FailedAssertionExceptionProvider(Expression<Func<bool>> assertion)
    {
      _assertion = assertion;
    }

    internal Exception GetException()
    {
      var partsProvider = new AssertionPartProvider(_assertion);

      var failedParts = partsProvider.GetFailedAssertions();

      var failedAssertions = new List<FailedAssertion>(failedParts.Length);

      foreach (var part in failedParts)
      {
        var failedExpressionString = ExpressionHelper.SanitizeExpressionString(part.Expression.ToString());

        if (part.Exception == null)
        {
          var friendlyMessageProvider = new FriendlyMessageProvider(part);

          var friendlyMessage = friendlyMessageProvider.TryGetFriendlyMessage();

          var friendlyMessageString = ExpressionHelper.SanitizeExpressionString(friendlyMessage?.Message);

          string fullMessage;

          if (friendlyMessageString != null)
          {
            fullMessage =
              $@"{friendlyMessageString}

Assertion: {failedExpressionString}";
          }
          else
          {
            fullMessage = $@"Assertion failed: {failedExpressionString}";
          }

          var failedAssertion = new FailedAssertion(part, fullMessage, friendlyMessage?.Pattern);

          failedAssertions.Add(failedAssertion);
        }
        else
        {
          var fullMessage = $@"Assertion threw {part.Exception.GetType().FullName}: {part.Exception.Message}

Assertion: {failedExpressionString}";
          
          var failedAssertion = new FailedAssertion(part, fullMessage, null);

          failedAssertions.Add(failedAssertion);
        }
      }

      var message = string.Join(Environment.NewLine, failedAssertions.Select(f => f.Message));

      return ExceptionHelper.GetException(message);
    }
  }
}
