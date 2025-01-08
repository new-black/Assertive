using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Assertive.Config;
using Assertive.xUnit;
using DiffEngine;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: EnableAssertiveSnapshots]

namespace Assertive.Test.Properties;

public static class GlobalSetup
{
  [ModuleInitializer]
  public static void Initialize()
  {
    Configuration.Snapshots.LaunchDiffTool = (temp, target) =>
    {
      DiffRunner.Launch(temp, target);
    };

    //Configuration.Snapshots.ExtraneousProperties = (_, _) => Configuration.ExtraneousPropertiesOptions.AutomaticUpdate;
    Configuration.Snapshots.ExcludeNullValues = true;

    DirectoryInfo? baseDir = null;

    //Configuration.Snapshots.AssumeCorrectness = true;

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