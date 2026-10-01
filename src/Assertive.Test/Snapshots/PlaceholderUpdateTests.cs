using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class PlaceholderUpdateTests
{
  // When snapshots are regenerated (TreatAllSnapshotsAsCorrect / ASSERTIVE_ACCEPT_SNAPSHOT_CHANGES),
  // placeholders that still match the actual value must be preserved instead of being replaced by
  // the unstable values they mask. Expected files are redirected to a throwaway directory so no
  // tracked snapshot is touched.
  [Fact]
  public void Auto_update_keeps_placeholders_that_still_match()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"assertive_placeholder_update_{Path.GetRandomFileName()}");
    Directory.CreateDirectory(tempDir);

    try
    {
      var config = Configuration.Snapshots with
      {
        ExpectedFileDirectoryResolver = (_, _) => tempDir,
        IncludeCounterForExplicitSnapshotIdentifiers = false
      };

      Assert(new { ProductId = Random.Shared.NextInt64(), Name = "Widget" },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "product",
          Configuration = config with { AcceptNewSnapshots = true }
        });

      var expectedFilePath = Directory.GetFiles(tempDir, "*.expected.json").Single();
      var baseline = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      baseline["ProductId"] = "@@productid";
      File.WriteAllText(expectedFilePath, baseline.ToJsonString());

      var newProductId = Random.Shared.NextInt64();

      Assert(new { ProductId = newProductId, Name = "Gadget", Sku = "ABC-123" },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "product",
          Configuration = config with { TreatAllSnapshotsAsCorrect = true }
        });

      var updated = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;

      Assert(() => updated["ProductId"]!.GetValue<string>() == "@@productid");
      Assert(() => updated["Name"]!.GetValue<string>() == "Gadget");
      Assert(() => updated["Sku"]!.GetValue<string>() == "ABC-123");
      Assert(() => !updated.ToJsonString().Contains(newProductId.ToString()));
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact]
  public void Auto_update_keeps_numbered_placeholders_while_values_stay_equal()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"assertive_placeholder_update_{Path.GetRandomFileName()}");
    Directory.CreateDirectory(tempDir);

    try
    {
      var config = Configuration.Snapshots with
      {
        ExpectedFileDirectoryResolver = (_, _) => tempDir,
        IncludeCounterForExplicitSnapshotIdentifiers = false
      };

      Assert(new { BeforePrice = 10m, AfterPrice = 10m },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "prices",
          Configuration = config with { AcceptNewSnapshots = true }
        });

      var expectedFilePath = Directory.GetFiles(tempDir, "*.expected.json").Single();
      var baseline = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      baseline["BeforePrice"] = "@@price#1";
      baseline["AfterPrice"] = "@@price#1";
      File.WriteAllText(expectedFilePath, baseline.ToJsonString());

      Assert(new { BeforePrice = 12m, AfterPrice = 12m },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "prices",
          Configuration = config with { TreatAllSnapshotsAsCorrect = true }
        });

      var equalUpdate = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;

      Assert(() => equalUpdate["BeforePrice"]!.GetValue<string>() == "@@price#1"
                   && equalUpdate["AfterPrice"]!.GetValue<string>() == "@@price#1");

      // Diverging values: the first occurrence keeps the placeholder, the inconsistent one is
      // replaced by its actual value (mirroring the PlaceholderCountMismatch comparison rule).
      Assert(new { BeforePrice = 15m, AfterPrice = 16m },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "prices",
          Configuration = config with { TreatAllSnapshotsAsCorrect = true }
        });

      var divergedUpdate = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;

      Assert(() => divergedUpdate["BeforePrice"]!.GetValue<string>() == "@@price#1"
                   && divergedUpdate["AfterPrice"]!.GetValue<decimal>() == 16m);
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact]
  public void Auto_update_replaces_placeholder_when_validator_no_longer_matches()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"assertive_placeholder_update_{Path.GetRandomFileName()}");
    Directory.CreateDirectory(tempDir);

    try
    {
      var config = Configuration.Snapshots with
      {
        ExpectedFileDirectoryResolver = (_, _) => tempDir,
        IncludeCounterForExplicitSnapshotIdentifiers = false
      };

      config.Normalization.RegisterPlaceholderValidator("positiveprice",
        value => decimal.TryParse(value, out var price) && price > 0, "Price must be positive");

      Assert(new { Price = 9.99m },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "product",
          Configuration = config with { AcceptNewSnapshots = true }
        });

      var expectedFilePath = Directory.GetFiles(tempDir, "*.expected.json").Single();
      var baseline = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      baseline["Price"] = "@@positiveprice";
      File.WriteAllText(expectedFilePath, baseline.ToJsonString());

      Assert(new { Price = -5m },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "product",
          Configuration = config with { TreatAllSnapshotsAsCorrect = true }
        });

      var updated = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;

      Assert(() => updated["Price"]!.GetValue<decimal>() == -5m);
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact]
  public void Auto_update_aligns_arrays_by_index_when_preserving_placeholders()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), $"assertive_placeholder_update_{Path.GetRandomFileName()}");
    Directory.CreateDirectory(tempDir);

    try
    {
      var config = Configuration.Snapshots with
      {
        ExpectedFileDirectoryResolver = (_, _) => tempDir,
        IncludeCounterForExplicitSnapshotIdentifiers = false
      };

      Assert(new { Ids = new[] { 1L, 2L } },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "ids",
          Configuration = config with { AcceptNewSnapshots = true }
        });

      var expectedFilePath = Directory.GetFiles(tempDir, "*.expected.json").Single();
      var baseline = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      baseline["Ids"] = new JsonArray("@@id", 2L);
      File.WriteAllText(expectedFilePath, baseline.ToJsonString());

      var newId = Random.Shared.NextInt64();

      Assert(new { Ids = new[] { newId, 2L, 3L } },
        new AssertSnapshotOptions
        {
          SnapshotIdentifier = "ids",
          Configuration = config with { TreatAllSnapshotsAsCorrect = true }
        });

      var updated = (JsonObject)JsonNode.Parse(File.ReadAllText(expectedFilePath))!;
      var ids = (JsonArray)updated["Ids"]!;

      Assert(() => ids[0]!.GetValue<string>() == "@@id"
                   && ids[1]!.GetValue<long>() == 2L
                   && ids[2]!.GetValue<long>() == 3L
                   && !updated.ToJsonString().Contains(newId.ToString()));
    }
    finally
    {
      Directory.Delete(tempDir, recursive: true);
    }
  }
}
