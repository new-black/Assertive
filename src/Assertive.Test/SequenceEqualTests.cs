using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Assertive.Test
{
  public class SequenceEqualTests : AssertionTestBase
  {
    [Fact]
    public void Int_sequence()
    {
      var seq1 = new int[] { 1, 2, 3 };
      var seq2 = new int[] { 1, 3, 3 };
      
      ShouldFail(() => seq1.SequenceEqual(seq2), "seq1 should equal seq2", @"There was 1 difference:

[1]: 2 <> 3

seq1: [ 1, 2, 3 ]
seq2: [ 1, 3, 3 ]");
    }

    private class MyClass : IEquatable<MyClass>
    {
      public MyClass(int id)
      {
        ID = id;
      }
      public int ID { get; set; }

      public bool Equals(MyClass? other)
      {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ID == other.ID;
      }

      public override bool Equals(object? obj)
      {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((MyClass)obj);
      }

      public override int GetHashCode()
      {
        return ID;
      }
    }

    [Fact]
    public void SequenceEquals_for_Equatable_class()
    {
      var seq1 = new[] { new MyClass(1), new MyClass(2), new MyClass(3) };
      var seq2 = new[] { new MyClass(1), new MyClass(3), new MyClass(2) };
      
      ShouldFail(() => seq1.SequenceEqual(seq2), "seq1 should equal seq2", @"There were 2 differences:

[1]: { ID = 2 } <> { ID = 3 },
[2]: { ID = 3 } <> { ID = 2 }

seq1: [ { ID = 1 }, { ID = 2 }, { ID = 3 } ]
seq2: [ { ID = 1 }, { ID = 3 }, { ID = 2 } ]");
    }
    
    [Fact]
    public void String_sequence()
    {
      var seq1 = new[] { "foo", "bar", "value" };
      var seq2 = new[] { "bar", "foo", null, "something" };
      
      ShouldFail(() => seq1.SequenceEqual(seq2), "seq1 should equal seq2", @"There were 4 differences:

[0]: ""foo"" <> ""bar"",
[1]: ""bar"" <> ""foo"",
[2]: ""value"" <> null,
[3]: (no value) <> ""something""

seq1: [ ""foo"", ""bar"", ""value"" ]
seq2: [ ""bar"", ""foo"", null, ""something"" ]");
    }
    
    [Fact]
    public void String_sequence_custom_comparer()
    {
      var seq1 = new[] { "a", "B", "c" };
      var seq2 = new[] { "A", "b", "D" };
      
      ShouldFail(() => seq1.SequenceEqual(seq2, StringComparer.OrdinalIgnoreCase), "seq1 should equal seq2", @"There was 1 difference:

[2]: ""c"" <> ""D""

seq1: [ ""a"", ""B"", ""c"" ]
seq2: [ ""A"", ""b"", ""D"" ]");
    }
    
    [Fact]
    public void Int_sequence_more_than_10()
    {
      var seq1 = new[] { 99 }.Concat(Enumerable.Range(1, 100));
      var seq2 = new[] { 13 }.Concat(Enumerable.Range(1, 100));
      
      ShouldFail(() => seq1.SequenceEqual(seq2), "seq1 should equal seq2", @"There was 1 difference:

[0]: 99 <> 13

seq1: [ 99, 1, 2, 3, 4, 5, 6, 7, 8, 9, ... ]
seq2: [ 13, 1, 2, 3, 4, 5, 6, 7, 8, 9, ... ]");
    }
    
    [Fact]
    public void Int_sequence_more_than_10_differences()
    {
      var seq1 = Enumerable.Range(100, 100);
      var seq2 = Enumerable.Range(0, 100);
      
      ShouldFail(() => seq1.SequenceEqual(seq2), "seq1 should equal seq2", @"There were 100 differences (first 10):

[0]: 100 <> 0,
[1]: 101 <> 1,
[2]: 102 <> 2,
[3]: 103 <> 3,
[4]: 104 <> 4,
[5]: 105 <> 5,
[6]: 106 <> 6,
[7]: 107 <> 7,
[8]: 108 <> 8,
[9]: 109 <> 9

seq1: [ 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, ... ]
seq2: [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ... ]");
    }

    [Fact]
    public void Dictionary_tests()
    {
      var dict1 = new Dictionary<int, string>()
      {
        [1] = "a",
        [2] = "b"
      };
      
      var dict2 = new Dictionary<int, string>()
      {
        [2] = "c",
        [3] = "d"
      };

      ShouldFail(() => dict1.SequenceEqual(dict2), "dict1 should equal dict2", @"There were 2 differences:

[0]: [1] = ""a"" <> [2] = ""c"",
[1]: [2] = ""b"" <> [3] = ""d""

dict1: [ [1] = ""a"", [2] = ""b"" ]
dict2: [ [2] = ""c"", [3] = ""d"" ]");
    }
  }
}