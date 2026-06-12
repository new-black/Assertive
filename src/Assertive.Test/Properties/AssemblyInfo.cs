using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    Configuration.Snapshots.AcceptNewSnapshots = true;

    Configuration.Snapshots.StringTransform = line =>
    {
      // Drop framework stack-trace frames: their presence and exact shape varies across
      // operating systems and runtime versions (e.g. inlined "at System.DateTime.Parse"),
      // which would make stack-trace snapshots non-deterministic between local and CI runs.
      // Keep the test's own frames (they are stable for a given source + compiler).
      if (Regex.IsMatch(line, @"^\s+at ") && !line.Contains("Assertive.Test"))
      {
        return null;
      }

      return Regex.Replace(line, @" in .+[/\\]([^/\\]+\.cs):line (\d+)", " in $1:line $2");
    };

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