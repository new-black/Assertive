using System.Runtime.CompilerServices;
using Assertive.Config;

namespace Assertive.Test.xUnit.v3;

public static class GlobalSetup
{
  [ModuleInitializer]
  public static void Initialize()
  {
    Configuration.Snapshots.ExcludeNullValues = true;

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
