namespace Assertive
{
  internal class FriendlyMessage
  {
    internal FriendlyMessage(string? message, IFriendlyMessagePattern pattern)
    {
      Message = message;
      Pattern = pattern;
    }

    public string? Message { get; set; }
    public IFriendlyMessagePattern Pattern { get; set; }
  }
}
