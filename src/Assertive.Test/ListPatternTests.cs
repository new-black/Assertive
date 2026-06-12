using System.Collections.Generic;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test
{
  /// <summary>
  /// Assertions using C# 11+ list patterns: arr is [1, 2, 3], arr is [_, > 0, _], etc.
  /// Decomposition: length check leaf + one leaf per non-discard, non-slice element pattern.
  /// Var captures (arr is [var first, ..]) bind in &&-chains and are available in subsequent conjuncts.
  /// </summary>
  public class ListPatternTests : AssertionTestBase
  {
    // --- Exact match (no slice) ---

    [Fact]
    public void Reports_length_failure_when_too_short()
    {
      var arr = new[] { 1, 2 };
      ShouldFail(() => arr is [1, 2, 3]);
    }

    [Fact]
    public void Reports_length_failure_when_too_long()
    {
      var arr = new[] { 1, 2, 3, 4 };
      ShouldFail(() => arr is [1, 2, 3]);
    }

    [Fact]
    public void Reports_element_failure_at_index_zero()
    {
      var arr = new[] { 9, 2, 3 };
      ShouldFail(() => arr is [1, 2, 3]);
    }

    [Fact]
    public void Reports_element_failure_at_last_index()
    {
      var arr = new[] { 1, 2, 9 };
      ShouldFail(() => arr is [1, 2, 3]);
    }

    [Fact]
    public void Passing_exact_match_does_not_throw()
    {
      var arr = new[] { 1, 2, 3 };
      Assert(() => arr is [1, 2, 3]);
    }

    // --- Empty list ---

    [Fact]
    public void Reports_empty_failure_when_not_empty()
    {
      var arr = new[] { 1 };
      ShouldFail(() => arr is []);
    }

    [Fact]
    public void Passing_empty_list_does_not_throw()
    {
      var arr = new int[0];
      Assert(() => arr is []);
    }

    // --- Discards ---

    [Fact]
    public void Reports_element_failure_skipping_discards()
    {
      var arr = new[] { 1, -5, 3 };
      ShouldFail(() => arr is [_, > 0, _]);
    }

    [Fact]
    public void Passing_pattern_with_discards_does_not_throw()
    {
      var arr = new[] { 99, 7, 42 };
      Assert(() => arr is [_, > 0, _]);
    }

    // --- Slice prefix (arr is [1, ..]) ---

    [Fact]
    public void Reports_length_failure_when_too_short_with_prefix_slice()
    {
      var arr = new[] { 1 };
      ShouldFail(() => arr is [1, 2, ..]);
    }

    [Fact]
    public void Reports_element_failure_with_prefix_slice()
    {
      var arr = new[] { 9, 2, 3, 4 };
      ShouldFail(() => arr is [1, ..]);
    }

    [Fact]
    public void Passing_prefix_slice_does_not_throw()
    {
      var arr = new[] { 1, 2, 3, 4 };
      Assert(() => arr is [1, ..]);
    }

    // --- Slice suffix (arr is [.., 3]) ---

    [Fact]
    public void Reports_element_failure_with_suffix_slice()
    {
      var arr = new[] { 1, 2, 9 };
      ShouldFail(() => arr is [.., 3]);
    }

    [Fact]
    public void Passing_suffix_slice_does_not_throw()
    {
      var arr = new[] { 1, 2, 3 };
      Assert(() => arr is [.., 3]);
    }

    // --- Relational element patterns ---

    [Fact]
    public void Reports_relational_element_failure()
    {
      var arr = new[] { 5, -1, 10 };
      ShouldFail(() => arr is [> 0, > 0, > 0]);
    }

    [Fact]
    public void Passing_relational_elements_do_not_throw()
    {
      var arr = new[] { 1, 2, 3 };
      Assert(() => arr is [> 0, > 0, > 0]);
    }

    // --- Null element patterns ---

    [Fact]
    public void Reports_null_element_failure_when_not_null()
    {
      var arr = new string?[] { "hello", null };
      ShouldFail(() => arr is [null, null]);
    }

    [Fact]
    public void Reports_not_null_element_failure_when_null()
    {
      var arr = new string?[] { null, "world" };
      ShouldFail(() => arr is [not null, not null]);
    }

    // --- List<T> with Count ---

    [Fact]
    public void Reports_length_failure_for_List()
    {
      var list = new List<int> { 1, 2 };
      ShouldFail(() => list is [1, 2, 3]);
    }

    [Fact]
    public void Reports_element_failure_for_List()
    {
      var list = new List<int> { 1, 9, 3 };
      ShouldFail(() => list is [1, 2, 3]);
    }

    [Fact]
    public void Passing_List_pattern_does_not_throw()
    {
      var list = new List<int> { 1, 2, 3 };
      Assert(() => list is [1, 2, 3]);
    }

    // --- Var capture in &&-chain ---

    [Fact]
    public void Var_capture_subsequent_failure_is_reported()
    {
      var items = new[] { 1, 2, 3 };
      ShouldFail(() => items is [var first, ..] && first > 5);
    }

    [Fact]
    public void Var_capture_length_failure_is_reported()
    {
      var items = new int[0];
      ShouldFail(() => items is [var first, ..] && first > 0);
    }

    [Fact]
    public void Passing_var_capture_chain_does_not_throw()
    {
      var items = new[] { 10, 20, 30 };
      Assert(() => items is [var first, ..] && first > 5);
    }

    // --- List pattern in &&-chain (not the whole assertion) ---

    [Fact]
    public void List_pattern_as_conjunct_reports_correct_conjunct()
    {
      var arr = new[] { 1, 2, 3 };
      var flag = true;
      ShouldFail(() => flag && arr is [1, 2, 99]);
    }
  }
}
