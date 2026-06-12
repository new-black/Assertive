using Xunit;

namespace Assertive.Test
{
  /// <summary>
  /// Tuple literals as assertion operands — a shape the old expression-tree engine could
  /// never decompose (tuple equality does not exist in expression trees). Typed
  /// compilation pastes the literal as written; the reflective fallback reconstructs the
  /// tuple element-wise, casting each compiled element back to its static type. Whole
  /// tuples display via ValueTuple.ToString(), so string elements are unquoted and null
  /// elements are empty.
  /// </summary>
  public class TupleLiteralTests : AssertionTestBase
  {
    [Fact]
    public void Tuple_literal_equality_is_decomposed()
    {
      var a = 1;
      var b = 2;

      ShouldFail(() => (a, b) == (1, 5));
    }

    [Fact]
    public void Named_tuple_elements_paste_as_written()
    {
      var a = 1;

      // The element names (x:, y:) are not free-standing references; the typed paste
      // keeps them verbatim.
      ShouldFail(() => (x: a, y: 2) == (3, 2));
    }

    [Fact]
    public void Tuple_literal_inequality_is_decomposed()
    {
      var a = 1;

      ShouldFail(() => (a, 2) != (1, 2));
    }

    [Fact]
    public void Nested_tuple_literals_are_decomposed()
    {
      var a = 1;

      ShouldFail(() => ((a, 2), 3) == ((9, 2), 3));
    }

    [Fact]
    public void Tuple_local_against_literal_is_decomposed()
    {
      var t = (1, 2);

      ShouldFail(() => t == (1, 5));
    }

    [Fact]
    public void Tuple_literal_with_string_elements()
    {
      var name = "foo";

      ShouldFail(() => (name, 1) == ("bar", 1));
    }

    [Fact]
    public void Tuple_with_private_method_element_is_reconstructed_reflectively()
    {
      // GetValue is private, so the typed paste rejects the fragment; the reflective
      // strategy rebuilds the tuple around GeneratedAssert.InvokeStatic.
      ShouldFail(() => (GetValue(), 2) == (9, 2));
    }

    [Fact]
    public void Tuple_element_with_implicit_conversion_does_not_unbox_mismatch()
    {
      // Tuple equality converts GetValue() to long; the reflective reconstruction must
      // cast the boxed element to its natural type (int), not the converted one.
      ShouldFail(() => (GetValue(), 2) == (9L, 2L));
    }

    [Fact]
    public void Tuple_with_null_element_is_reconstructed_reflectively()
    {
      // A null literal has no natural type; the element cast falls back to the
      // converted type (string), a reference conversion that null survives.
      ShouldFail(() => (GetValue(), null) == (9, "x"));
    }

    private static int GetValue() => 7;
  }
}
