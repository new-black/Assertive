using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class AutomaticUpdateTests
{
  // AutomaticUpdate adds properties that exist on the actual object but not in the expected
  // snapshot back into the expected file on disk. To test it without mutating a snapshot that's
  // tracked in source control, we redirect the expected file to a throwaway temp directory via
  // ExpectedFileDirectoryResolver, so the file that gets rewritten lives entirely outside the repo.
  [Fact]
  public void AutomaticUpdate_adds_extraneous_properties_to_the_expected_file()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"assertive_automatic_update_{Path.GetRandomFileName()}");
    Directory.CreateDirectory(tempDir);

    try
    {
      var config = Configuration.Snapshots with
      {
        ExpectedFileDirectoryResolver = (_, _) => tempDir,
        IncludeCounterForExplicitSnapshotIdentifiers = false
      };

      // Step 1: establish a baseline expected file containing only Name and Price.
      // AcceptNewSnapshots writes the expected file since none exists yet.
      var baseline = new { Name = "Widget", Price = 9.99m };
      Assert(baseline, new AssertSnapshotOptions
      {
        SnapshotIdentifier = "product",
        Configuration = config with { AcceptNewSnapshots = true }
      });

      var expectedFilePath = Directory.GetFiles(tempDir, "*.expected.json").Single();

      // Sanity check: the baseline file does not yet contain the extra property.
      var baselineJson = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      Assert(() => !baselineJson.ContainsKey("Sku"));

      // Step 2: assert an object that has an additional property (Sku) not in the expected file,
      // with AutomaticUpdate enabled. The existing properties match, so the only difference is the
      // extraneous Sku property, which should be added to the expected file rather than fail.
      var updated = new { Name = "Widget", Price = 9.99m, Sku = "ABC-123" };
      Assert(updated, new AssertSnapshotOptions
      {
        SnapshotIdentifier = "product",
        Configuration = config with
        {
          ExtraneousProperties = (_, _) => Configuration.ExtraneousPropertiesOptions.AutomaticUpdate
        }
      });

      // The expected file on disk should now include the new property with its value, while the
      // original properties are preserved.
      var updatedJson = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      Assert(() => updatedJson.ContainsKey("Sku")
                   && updatedJson["Sku"]!.GetValue<string>() == "ABC-123"
                   && updatedJson["Name"]!.GetValue<string>() == "Widget");
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }
}
