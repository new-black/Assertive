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

          const string Reset = "\u001b[0m";
          //const string Bold = "\u001b[1m";
          const string Red = "\u001b[31m";
          const string Green = "\u001b[32m";
          //const string Yellow = "\u001b[33m";
          //const string Cyan = "\u001b[36m";

          static string Color(string text, string colorCode)
          {
            return $"{colorCode}{text}{Reset}";
          }

          if (expectedAndActual == null)
          {
            friendlyMessage = null;
          }
          else if (expectedAndActual.Actual != null)
          {
            friendlyMessage = $"""
                               {Color("[EXPECTED]", Green)}

                               {expectedAndActual.Expected}

                               {Color("[ACTUAL]", Red)}

                               {expectedAndActual.Actual}
                               """;
          }
          else
          {
            friendlyMessage = $"""
                               [EXPECTED]

                               {expectedAndActual.Expected}
                               """;
          }

          var formattedMessage = FriendlyMessageFormatter.GetString(friendlyMessage, _context.EvaluatedExpressions);

          return new FriendlyMessage(formattedMessage, pattern);
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