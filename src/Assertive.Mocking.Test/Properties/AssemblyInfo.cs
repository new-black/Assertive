using System.IO;
using System.Runtime.CompilerServices;
using Assertive.Config;
using Assertive.xUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: EnableAssertiveSnapshots]

namespace Assertive.Mocking.Test.Properties;

public static class GlobalSetup
{
  [ModuleInitializer]
  public static void Initialize()
  {
    // Snapshots must be committed explicitly; a new/unseen snapshot fails the build instead of
    // being silently accepted (previously this hid a snapshot-format migration and orphaned files).
    Configuration.Snapshots.AcceptNewSnapshots = false;

    DirectoryInfo? baseDir = null;

    Configuration.Snapshots.ExpectedFileDirectoryResolver = (method, file) =>
    {
      if (baseDir == null)
      {
        var dir = file.Directory;

        while (dir != null)
        {
          if (dir.Name == "src")
          {
            baseDir = dir;
            break;
          }

          dir = dir.Parent;
        }
      }

      return Path.Combine(baseDir!.FullName, "Snapshots", method.Module.Assembly.GetName().Name!);
    };
  }
}
