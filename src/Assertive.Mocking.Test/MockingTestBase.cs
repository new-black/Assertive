using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Assertive.Config;

public abstract partial class MockingTestBase
{
  private static partial class AnsiHelper
  {
    [GeneratedRegex(@"\[[0-9;]*[A-Za-z]")]
    public static partial Regex AnsiRegex();
  }

  private static string StripAnsi(string input)
    => AnsiHelper.AnsiRegex().Replace(input, "").Replace("\r\n", "\n").Replace("\r", "\n");

  private static string HashExpression(string expression)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expression)))[..8];

  protected static void ShouldFail(
    Action action,
    [CallerArgumentExpression(nameof(action))] string actionExpression = "",
    [CallerFilePath] string callerFilePath = "")
  {
    var original = Configuration.Colors.Enabled;
    Configuration.Colors.Enabled = false;
    Exception ex;
    try
    {
      ex = Xunit.Assert.ThrowsAny<Exception>(action);
    }
    finally
    {
      Configuration.Colors.Enabled = original;
    }

    Assertive.Assert.Snapshot(StripAnsi(ex.Message),
      options: HashExpression(actionExpression),
      expression: actionExpression,
      sourceFile: callerFilePath);
  }
}
