using System;
using Assertive.Interfaces;
using Assertive.Patterns;

namespace Assertive.Analyzers
{
  internal class FriendlyMessageProvider
  {
    private readonly AssertionFailureContext _context;
    private readonly FailedAssertion _part;

    private static readonly IFriendlyMessagePattern _fallbackPattern = new FallbackPattern();

    public FriendlyMessageProvider(AssertionFailureContext context, FailedAssertion part)
    {
      _context = context;
      _part = part;
    }

    private FriendlyMessage? EvaluatePattern(IFriendlyMessagePattern pattern)
    {
      try
      {
        if (pattern.IsMatch(_part))
        {
          foreach (var subPattern in pattern.SubPatterns)
          {
            var message = EvaluatePattern(subPattern);

            if (message != null)
            {
              return message;
            }
          }

          var expectedAndActual = pattern.TryGetFriendlyMessage(_part);

          FormattableString? friendlyMessage;

          var colors = Config.Configuration.Colors;

          if (expectedAndActual == null)
          {
            friendlyMessage = null;
          }
          else if (expectedAndActual.Actual != null)
          {
            friendlyMessage = $"""
                               {colors.ExpectedHeader()}
                               {expectedAndActual.Expected}
                               {colors.ActualHeader()}
                               {expectedAndActual.Actual}
                               
                               """;
          }
          else
          {
            friendlyMessage = $"""
                               {colors.ExpectedHeader()}
                               {expectedAndActual.Expected}
                               
                               """;
          }

          var formattedMessage = FriendlyMessageFormatter.GetString(friendlyMessage, _context.EvaluatedExpressions);

          return new FriendlyMessage(formattedMessage, pattern, expectedAndActual);
        }
      }
      catch
      {
        return null;
      }

      return null;
    }

    public FriendlyMessage? TryGetFriendlyMessage()
    {
      var message = EvaluatePattern(_fallbackPattern);
      return message;
    }
  }
}