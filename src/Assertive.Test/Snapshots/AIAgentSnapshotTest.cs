using System.Collections.Generic;
using System.IO;
using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class AIAgentSnapshotTest
{
  [Fact]
  public void AcceptNewSnapshots_auto_accepts_new_snapshots()
  {
    // The snapshot system creates files relative to the source file location
    // Find the expected file by searching from the test source directory
    var sourceDir = Path.GetDirectoryName(GetSourceFilePath())!;
    var snapshotsDir = Path.Combine(sourceDir, "..", "..", "Snapshots", "Assertive.Test");

    var expectedFileName = "Assertive.Test.Snapshots.AIAgentSnapshotTest.AcceptNewSnapshots_auto_accepts_new_snapshots#product_1.expected.json";
    var expectedFilePath = Path.Combine(snapshotsDir, expectedFileName);

    // Delete the file if it exists to simulate a new snapshot
    if (File.Exists(expectedFilePath))
    {
      File.Delete(expectedFilePath);
    }

    var originalValue = Configuration.Snapshots.AcceptNewSnapshots;

    try
    {
      Configuration.Snapshots.AcceptNewSnapshots = true;

      var product = new { Name = "Test Product", Price = 19.99m };

      // This should pass even though no expected file exists, because AcceptNewSnapshots is true
      Assert(product);

      // Verify the expected file was created
      Assertive.Assert.That(() => File.Exists(expectedFilePath));
    }
    finally
    {
      Configuration.Snapshots.AcceptNewSnapshots = originalValue;
    }
  }

  private static string GetSourceFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
