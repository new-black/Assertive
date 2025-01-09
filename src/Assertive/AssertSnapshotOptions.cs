using Assertive.Config;

namespace Assertive;

public class AssertSnapshotOptions
{
  public string? SnapshotIdentifier { get; set; }
  public Configuration.CompareSnapshotsConfiguration Configuration { get; set; } = Config.Configuration.Snapshots;
  
  internal static AssertSnapshotOptions Default => new ();
  
  public static implicit operator AssertSnapshotOptions(Configuration.CompareSnapshotsConfiguration configuration)
  {
    return new AssertSnapshotOptions { Configuration = configuration };
  }
  
  public static implicit operator AssertSnapshotOptions(string identifier)
  {
    return new AssertSnapshotOptions { SnapshotIdentifier = identifier };
  }
}