using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Method calls with named arguments inside assertion bodies.
  /// Key invariant: argument labels (param: value) are structural names, not local
  /// variable references — they must not appear in the LOCALS section.
  /// Actual captured locals must still appear correctly.
  /// </summary>
  public class NamedArgumentTests : AssertionTestBase
  {
    private static int Add(int x, int y) => x + y;
    private static bool StartsWith(string value, string prefix) => value.StartsWith(prefix);

    [Fact]
    public void Named_argument_call_is_decomposed()
    {
      var x = 3;
      ShouldFail(() => Add(x: x, y: 2) == 10);
    }

    [Fact]
    public void Argument_label_matching_local_name_is_not_double_captured()
    {
      // 'x' is both the argument label and the local variable name.
      // It should appear once in locals (as the captured variable), not twice.
      var x = 3;
      ShouldFail(() => Add(x: x, y: 2) == 10);
    }

    [Fact]
    public void Named_bool_method_call_is_decomposed()
    {
      var prefix = "world";
      ShouldFail(() => StartsWith(value: "hello", prefix: prefix));
    }

    [Fact]
    public void Passing_named_argument_call_does_not_throw()
    {
      var x = 4;
      Assert(() => Add(x: x, y: 6) == 10);
    }
  }
}
