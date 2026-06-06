using System.Collections.Generic;
using Assertive.Config;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class ProjectionTests
{
  private class Student
  {
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public List<string> Grades { get; set; } = new();
  }

  private class School
  {
    public string Name { get; set; } = "";
    public Student Principal { get; set; } = new();
    public List<Student> Students { get; set; } = new();
  }

  private class TreeNode
  {
    public string Name { get; set; } = "";
    public int Weight { get; set; }
    public List<TreeNode> Children { get; set; } = new();
  }

  private static Configuration.SnapshotProjection NameOnly => obj => obj switch
  {
    Student s => new { Name = s.FullName },
    _ => obj
  };

  [Fact]
  public void Projects_root_value()
  {
    var student = new Student { FullName = "John Doe", Email = "john@example.com", Age = 20 };

    Assert(student, new AssertSnapshotOptions { Project = NameOnly });
  }

  [Fact]
  public void Projects_nested_property_values()
  {
    var school = new School
    {
      Name = "Test High",
      Principal = new Student { FullName = "Jane Smith", Email = "jane@example.com", Age = 45 }
    };

    Assert(school, new AssertSnapshotOptions { Project = NameOnly });
  }

  [Fact]
  public void Projects_elements_inside_collections()
  {
    var school = new School
    {
      Name = "Test High",
      Principal = new Student { FullName = "Jane Smith", Email = "jane@example.com", Age = 45 },
      Students =
      {
        new Student { FullName = "Alice", Email = "alice@x.com", Age = 18, Grades = { "A", "B" } },
        new Student { FullName = "Bob", Email = "bob@x.com", Age = 19, Grades = { "C" } }
      }
    };

    Assert(school, new AssertSnapshotOptions { Project = NameOnly });
  }

  [Fact]
  public void Projects_recursively_through_nested_trees()
  {
    var tree = new TreeNode
    {
      Name = "root",
      Weight = 100,
      Children =
      {
        new TreeNode
        {
          Name = "a",
          Weight = 10,
          Children = { new TreeNode { Name = "a1", Weight = 1 } }
        },
        new TreeNode { Name = "b", Weight = 20 }
      }
    };

    Assert(tree, new AssertSnapshotOptions
    {
      Project = obj => obj switch
      {
        TreeNode n => new { n.Name, n.Children },
        _ => obj
      }
    });
  }

  class DifferentRoot
  {
    public TreeNode Tree { get; set; }
  }
  
  [Fact]
  public void Projects_recursively_through_nested_trees_when_root_is_not_matched_by_switch()
  {
    var tree = new TreeNode
    {
      Name = "root",
      Weight = 100,
      Children =
      {
        new TreeNode
        {
          Name = "a",
          Weight = 10,
          Children = { new TreeNode { Name = "a1", Weight = 1 } }
        },
        new TreeNode { Name = "b", Weight = 20 }
      }
    };

    var root = new DifferentRoot { Tree = tree };

    Assert(root, new AssertSnapshotOptions
    {
      Project = obj => obj switch
      {
        TreeNode n => new { n.Name, n.Children },
        _ => obj
      }
    });
  }

  [Fact]
  public void Identity_projection_leaves_snapshot_unchanged()
  {
    var school = new School
    {
      Name = "Test High",
      Principal = new Student { FullName = "Jane Smith", Email = "jane@example.com", Age = 45 }
    };

    Assert(school, new AssertSnapshotOptions { Project = obj => obj });
  }
}
