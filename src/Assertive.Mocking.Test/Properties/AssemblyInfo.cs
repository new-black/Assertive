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
    Configuration.Snapshots.AcceptNewSnapshots = true;

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
