namespace Assertive.TestFrameworks;

internal class CurrentTestInfo
{
  public required string Name { get; set; }
  public required string ClassName { get; set; }
  public required object[]? Arguments { get; set; }
  public required object State { get; set; } 
}