using Assertive.Interfaces;

namespace Assertive.Analyzers
{
  internal class FailedAnalyzedAssertion
  {
    internal FailedAnalyzedAssertion(FailedAssertion part, string? message, IFriendlyMessagePattern? friendlyMessagePattern, ExpectedAndActual? expectedAndActual)
    {
      Assertion = part;
      Message = message;
      FriendlyMessagePattern = friendlyMessagePattern;
      ExpectedAndActual = expectedAndActual;
    }
    
    internal FailedAnalyzedAssertion(FailedAssertion part, string? message, IExceptionHandlerPattern? exceptionHandlerPattern, HandledException? handledException)
    {
      Assertion = part;
      Message = message;
      ExceptionHandlerPattern = exceptionHandlerPattern;
      HandledException = handledException;
    }
    
    public FailedAssertion Assertion { get; }
    public string? Message { get; }
    public IExceptionHandlerPattern? ExceptionHandlerPattern { get; }
    public IFriendlyMessagePattern? FriendlyMessagePattern { get; }
    public ExpectedAndActual? ExpectedAndActual { get; }
    public HandledException? HandledException { get; }
  }
}
