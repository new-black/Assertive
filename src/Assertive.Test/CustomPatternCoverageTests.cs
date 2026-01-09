using System;
using System.Collections.Generic;
using System.Linq;
using Assertive.Config;
using Assertive.Plugin;
using Xunit;

namespace Assertive.Test
{
  /// <summary>
  /// Tests that attempt to replicate each built-in pattern using custom patterns.
  /// These tests help identify gaps in the custom pattern DSL.
  /// </summary>
  public class CustomPatternCoverageTests : AssertionTestBase, IDisposable
  {
    public CustomPatternCoverageTests()
    {
      Configuration.Patterns.Clear();
    }

    public void Dispose()
    {
      Configuration.Patterns.Clear();
    }

    #region BoolPattern - Property access returning bool

    /// <summary>
    /// BoolPattern matches simple boolean member expressions.
    /// Custom pattern CAN replicate this for specific properties.
    /// </summary>
    [Fact]
    public void BoolPattern_PropertyAccess_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Property = new PropertyMatch { Name = "IsActive" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance}.IsActive should be true.",
          Actual = "It was false."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance}.IsActive should be false.",
          Actual = "It was true."
        }
      });

      var user = new User { IsActive = false };
      ShouldFail(() => user.IsActive, "user.IsActive should be true.", "It was false.");
    }

    [Fact]
    public void BoolPattern_PropertyAccess_Negated_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Property = new PropertyMatch { Name = "IsActive" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance}.IsActive should be true.",
          Actual = "It was false."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance}.IsActive should be false.",
          Actual = "It was true."
        }
      });

      var user = new User { IsActive = true };
      ShouldFail(() => !user.IsActive, "user.IsActive should be false.", "It was true.");
    }

    #endregion

    #region ContainsPattern - Collection/string contains

    /// <summary>
    /// ContainsPattern matches Enumerable.Contains and string.Contains.
    /// Custom pattern CAN replicate this.
    /// </summary>
    [Fact]
    public void ContainsPattern_Collection_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "Contains" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should contain {arg0.value}.",
          Actual = "It did not. Contents: {instance.value}"
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not contain {arg0.value}.",
          Actual = "It did."
        }
      });

      var list = new List<string> { "a", "b", "c" };
      ShouldFail(() => list.Contains("z"), "list should contain \"z\".", "It did not.");
    }

    [Fact]
    public void ContainsPattern_String_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [
          new MatchPredicate { Method = new MethodMatch { Name = "Contains" } },
          new MatchPredicate { InstanceType = "String" }
        ],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should contain substring {arg0.value}.",
          Actual = "Value: {instance.value}"
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not contain substring {arg0.value}.",
          Actual = "Value: {instance.value}"
        }
      });

      var text = "hello world";
      ShouldFail(() => text.Contains("xyz"), "text should contain substring \"xyz\".", "Value: \"hello world\"");
    }

    #endregion

    #region AnyPattern - Collection has items matching filter

    /// <summary>
    /// AnyPattern matches Enumerable.Any().
    /// Custom pattern CAN replicate basic case but NOT the lambda filter details.
    /// </summary>
    [Fact]
    public void AnyPattern_NoFilter_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "Any", ParameterCount = 0 } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should contain at least one item.",
          Actual = "It was empty."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "Collection {instance} should be empty.",
          Actual = "It contained {instance.count} items."
        }
      });

      var list = new List<string>();
      ShouldFail(() => list.Any(), "Collection list should contain at least one item.", "It was empty.");
    }

    [Fact]
    public void AnyPattern_WithFilter_CanShowLambda()
    {
      // {arg0} renders the lambda expression as text
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "Any", ParameterCount = 1 } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "Collection {instance} should contain items matching {arg0}.",
          Actual = "No items matched."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "Collection {instance} should not contain items matching {arg0}.",
          Actual = "Some items matched."
        }
      });

      var list = new List<int> { 1, 2, 3 };
      ShouldFail(() => list.Any(x => x > 10), "Collection list should contain items matching x => x > 10.", "No items matched.");
    }

    #endregion

    #region StartsWithAndEndsWithPattern - String prefix/suffix

    /// <summary>
    /// StartsWithAndEndsWithPattern matches string.StartsWith/EndsWith.
    /// Custom pattern CAN replicate this.
    /// </summary>
    [Fact]
    public void StartsWithPattern_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "StartsWith" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should start with {arg0.value}.",
          Actual = "Value: {instance.value}"
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not start with {arg0.value}.",
          Actual = "Value: {instance.value}"
        }
      });

      var text = "hello world";
      ShouldFail(() => text.StartsWith("xyz"), "text should start with \"xyz\".", "Value: \"hello world\"");
    }

    [Fact]
    public void EndsWithPattern_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "EndsWith" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should end with {arg0.value}.",
          Actual = "Value: {instance.value}"
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not end with {arg0.value}.",
          Actual = "Value: {instance.value}"
        }
      });

      var text = "hello world";
      ShouldFail(() => text.EndsWith("xyz"), "text should end with \"xyz\".", "Value: \"hello world\"");
    }

    #endregion

    #region SequenceEqualPattern - Collection equality

    /// <summary>
    /// SequenceEqualPattern matches Enumerable.SequenceEqual().
    /// Custom pattern can match but CANNOT show detailed differences.
    /// </summary>
    [Fact]
    public void SequenceEqualPattern_BasicMatch_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "SequenceEqual" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should equal {arg0}.",
          Actual = "Sequences differ."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not equal {arg0}.",
          Actual = "Sequences were equal."
        }
      });

      var list1 = new[] { 1, 2, 3 };
      var list2 = new[] { 1, 2, 4 };
      // NOTE: Cannot show detailed diff like built-in pattern does
      ShouldFail(() => list1.SequenceEqual(list2), "list1 should equal list2.", "Sequences differ.");
    }

    #endregion

    #region ReferenceEqualsPattern - Reference equality

    /// <summary>
    /// ReferenceEqualsPattern matches ReferenceEquals(a, b).
    /// Custom pattern CAN replicate this.
    /// </summary>
    [Fact]
    public void ReferenceEqualsPattern_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "ReferenceEquals" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{arg0} and {arg1} should be the same instance.",
          Actual = "They were different instances."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{arg0} and {arg1} should be different instances.",
          Actual = "They were the same instance."
        }
      });

      var obj1 = new object();
      var obj2 = new object();
      ShouldFail(() => ReferenceEquals(obj1, obj2), "obj1 and obj2 should be the same instance.", "They were different instances.");
    }

    #endregion

    #region AllPattern - All items match predicate

    /// <summary>
    /// AllPattern matches Enumerable.All().
    /// Custom pattern can match but CANNOT show which items failed or why.
    /// </summary>
    [Fact]
    public void AllPattern_CanShowLambda()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "All" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "All items in {instance} should match {arg0}.",
          Actual = "Some items did not match."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "Not all items in {instance} should match {arg0}.",
          Actual = "All items matched."
        }
      });

      var list = new List<int> { 1, 2, 3, 10, 20 };
      // NOTE: Cannot show which items failed like built-in pattern does
      ShouldFail(() => list.All(x => x < 5), "All items in list should match x => x < 5.", "Some items did not match.");
    }

    [Fact]
    public void AllPattern_CanShowFirstTenItems()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "All" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "All items in {instance} should match {arg0}.",
          Actual = "Items: {instance.firstTenItems}"
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "Not all items in {instance} should match {arg0}.",
          Actual = "All items matched."
        }
      });

      var list = new List<int> { 1, 2, 3, 10, 20 };
      ShouldFail(() => list.All(x => x < 5), "All items in list should match x => x < 5.", "Items: [1, 2, 3, 10, 20]");
    }

    [Fact]
    public void FirstItems_ShowsEllipsisWhenMoreThanTen()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
        AllowNegation = false,
        Output = new OutputDefinition
        {
          Expected = "{instance} should be empty.",
          Actual = "Items: {instance.firstTenItems}"
        }
      });

      var list = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
      ShouldFail(() => list.None(), "list should be empty.", "Items: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] ...");
    }

    #endregion

    #region ComparisonPattern/EqualityPattern - Binary expressions (==, !=, <, >, etc.)

    /// <summary>
    /// ComparisonPattern matches binary expressions like a == b, a < b.
    /// Custom pattern CANNOT replicate this - requires binary expression support.
    /// </summary>
    [Fact(Skip = "Binary expressions not supported in custom patterns")]
    public void EqualityPattern_CannotReplicate()
    {
      // This would require matching BinaryExpression with == operator
      // Current DSL only supports MethodCallExpression and MemberExpression
      var a = 5;
      var b = 10;
      ShouldFail(() => a == b, "a should equal b.", "a was 5, b was 10.");
    }

    [Fact(Skip = "Binary expressions not supported in custom patterns")]
    public void LessThanPattern_CannotReplicate()
    {
      // This would require matching BinaryExpression with < operator
      var a = 10;
      var b = 5;
      ShouldFail(() => a < b, "a should be less than b.", "a was 10, b was 5.");
    }

    #endregion

    #region IsPattern - Type checks (x is Type)

    /// <summary>
    /// IsPattern matches type binary expressions (x is Type).
    /// Custom pattern CANNOT replicate this - requires TypeBinaryExpression support.
    /// </summary>
    [Fact(Skip = "Type binary expressions not supported in custom patterns")]
    public void IsPattern_CannotReplicate()
    {
      // This would require matching TypeBinaryExpression
      object obj = "hello";
      ShouldFail(() => obj is int, "obj should be of type int.", "Type was String.");
    }

    #endregion

    #region NullPattern - Null checks (x == null, x != null)

    /// <summary>
    /// NullPattern matches null comparisons and 'is object' checks.
    /// Custom pattern CANNOT replicate this - requires binary expression support.
    /// </summary>
    [Fact(Skip = "Binary expressions not supported in custom patterns")]
    public void NullPattern_EqualityCheck_CannotReplicate()
    {
      // This would require matching BinaryExpression with null
      string? value = "not null";
      ShouldFail(() => value == null, "value should be null.", "Value: \"not null\"");
    }

    [Fact(Skip = "Type binary expressions not supported in custom patterns")]
    public void NullPattern_IsObject_CannotReplicate()
    {
      // This would require matching TypeBinaryExpression with 'object'
      string? value = null;
      ShouldFail(() => value is object, "value should not be null.", "It was null.");
    }

    #endregion

    #region HasValuePattern - Nullable HasValue property

    /// <summary>
    /// HasValuePattern matches Nullable<T>.HasValue property.
    /// Custom pattern CAN replicate this.
    /// </summary>
    [Fact]
    public void HasValuePattern_CanReplicate()
    {
      Configuration.Patterns.Register(new PatternDefinition
      {
        Match = [new MatchPredicate { Property = new PropertyMatch { Name = "HasValue" } }],
        AllowNegation = true,
        Output = new OutputDefinition
        {
          Expected = "{instance} should have a value.",
          Actual = "It was null."
        },
        OutputWhenNegated = new OutputDefinition
        {
          Expected = "{instance} should not have a value.",
          Actual = "Value: {instance.value}"
        }
      });

      int? value = null;
      ShouldFail(() => value.HasValue, "value should have a value.", "It was null.");
    }

    #endregion
  }

  public class User
  {
    public bool IsActive { get; set; }
    public string Name { get; set; } = "";
  }
}
