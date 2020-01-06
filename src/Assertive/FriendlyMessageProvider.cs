using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Assertive.Patterns;

namespace Assertive
{
  internal class FriendlyMessageProvider
  {
    private readonly Assertion _part;

    private static readonly IFriendlyMessagePattern _fallbackPattern = new FallbackPattern();

    public FriendlyMessageProvider(Assertion part)
    {
      _part = part;
    }

    private FriendlyMessage EvaluatePattern(IFriendlyMessagePattern pattern)
    {
      if (pattern.IsMatch(_part.Expression))
      {
        foreach (var subPattern in pattern.SubPatterns)
        {
          var message = EvaluatePattern(subPattern);

          if (message != null)
          {
            return message;
          }
        }

        return new FriendlyMessage(FriendlyMessageFormatter.GetString(pattern.TryGetFriendlyMessage(_part)), 
          pattern);
      }

      return null;
    }

    public FriendlyMessage TryGetFriendlyMessage()
    {
      var message = EvaluatePattern(_fallbackPattern);
      return message;
    }
  }
}