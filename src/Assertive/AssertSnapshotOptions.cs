using Assertive.Config;

namespace Assertive;

/// <summary>
/// Configuration options for snapshot assertions.
/// </summary>
public class AssertSnapshotOptions
{
  /// <summary>
  /// An optional identifier for the snapshot. When specified, this is used as the snapshot file name
  /// instead of deriving it from the test method name.
  /// </summary>
  public string? SnapshotIdentifier { get; set; }

  /// <summary>
  /// The configuration settings for snapshot comparison.
  /// Defaults to <see cref="Config.Configuration.Snapshots"/>.
  /// </summary>
  public Configuration.CompareSnapshotsConfiguration Configuration { get; set; } = Config.Configuration.Snapshots;

  /// <summary>
  /// An optional projection applied to every non-primitive, non-string object encountered while
  /// building the snapshot — the root value, each property value, and each element inside an enumerable.
  /// Use it to focus a snapshot on a few properties without writing a recursive walker by hand:
  /// <code>
  /// Assert(tree, new AssertSnapshotOptions
  /// {
  ///   Project = obj => obj switch
  ///   {
  ///     Student s =&gt; new { Name = s.FullName },
  ///     _ =&gt; obj
  ///   }
  /// });
  /// </code>
  /// Return the input value unchanged for a passthrough.
  /// </summary>
  public Configuration.SnapshotProjection? Project { get; set; }

  internal static AssertSnapshotOptions Default => new ();

  /// <summary>
  /// Allows passing a <see cref="Configuration.CompareSnapshotsConfiguration"/> directly
  /// where an <see cref="AssertSnapshotOptions"/> is expected.
  /// </summary>
  /// <param name="configuration">The snapshot comparison configuration.</param>
  public static implicit operator AssertSnapshotOptions(Configuration.CompareSnapshotsConfiguration configuration)
  {
    return new AssertSnapshotOptions { Configuration = configuration };
  }

  /// <summary>
  /// Allows passing a string identifier directly where an <see cref="AssertSnapshotOptions"/> is expected.
  /// </summary>
  /// <param name="identifier">The snapshot identifier.</param>
  public static implicit operator AssertSnapshotOptions(string identifier)
  {
    return new AssertSnapshotOptions { SnapshotIdentifier = identifier };
  }
}
