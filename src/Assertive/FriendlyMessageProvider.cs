using Assertive.Patterns;

namespace Assertive
{
  internal class FriendlyMessageProvider
  {
    private readonly FailedAssertion _part;

    private static readonly IFriendlyMessagePattern _fallbackPattern = new FallbackPattern();

    public FriendlyMessageProvider(FailedAssertion part)
    {
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

          return new FriendlyMessage(FriendlyMessageFormatter.GetString(pattern.TryGetFriendlyMessage(_part)),
            pattern);
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