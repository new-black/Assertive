using Assertive.Interfaces;

namespace Assertive.Analyzers
{
  internal class FriendlyMessage
  {
    internal FriendlyMessage(string? message, IFriendlyMessagePattern pattern, ExpectedAndActual? expectedAndActual)
    {
      Message = message;
      Pattern = pattern;
      ExpectedAndActual = expectedAndActual;
    }

    public string? Message { get; set; }
    public IFriendlyMessagePattern Pattern { get; set; }
    public ExpectedAndActual? ExpectedAndActual { get; }
  }
}
