using System.Reflection;

namespace Assertive.TestFrameworks;

internal class CurrentTestInfo
{
  public required MethodInfo Method { get; set; }
  public required string Name { get; set; }
  public required string ClassName { get; set; }
  public required object?[]? Arguments { get; set; }
  public required object State { get; set; } 
}