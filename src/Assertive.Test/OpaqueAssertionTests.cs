using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Expression-body lambdas whose form Assertive does not recognise are intercepted
  /// as Opaque: the failure shows the raw source text plus a LOCALS section with any
  /// captured variables. No decomposition is attempted.
  /// </summary>
  public class OpaqueAssertionTests : AssertionTestBase
  {
    [Fact]
    public void Unrecognized_expression_shows_source_and_locals()
    {
      var status = "active";
      ShouldFail(() => status switch { "inactive" => true, _ => false });
    }

    [Fact]
    public void Unrecognized_expression_with_multiple_locals()
    {
      var a = 3;
      var b = 7;
      ShouldFail(() => a switch { 1 => true, 2 => b > 10, _ => false });
    }
  }
}
