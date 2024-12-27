using Assertive.Config;

namespace Assertive;

public class AssertSnapshotOptions
{
  public string? SnapshotIdentifier { get; set; }
  public Configuration.CompareSnapshotsConfiguration Configuration { get; set; } = Config.Configuration.Snapshots;
  
  internal static AssertSnapshotOptions Default => new ();
}