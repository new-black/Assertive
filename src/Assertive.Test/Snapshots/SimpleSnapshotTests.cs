using System;
using System.Collections.Generic;
using Xunit;
using static Assertive.DSL;

namespace Assertive.Test.Snapshots;

public class SimpleSnapshotTests
{
  [Fact]
  public void Can_compare_instances_of_class()
  {
    var customer = new Customer { FirstName = "John", LastName = "Doe", Age = 42 };
    
    Assert(customer);
  }
  
  [Fact]
  public void Can_compare_multiple_distinct_expressions()
  {
    var customer1 = new Customer { FirstName = "John", LastName = "Doe", Age = 42 };
    
    Assert(customer1);
    
    var customer2 = new Customer { FirstName = "Jane", LastName = "Doe", Age = 50 };
    
    Assert(customer2);
  }
  
  [Fact]
  public void Using_the_same_expression_multiple_times_results_in_distinct_files()
  {
    var customer = new Customer { FirstName = "John", LastName = "Doe", Age = 42 };
    
    Assert(customer);
    
    customer = new Customer { FirstName = "Jane", LastName = "Doe", Age = 50 };
    
    Assert(customer);
  }
  
  [Fact]
  public void Can_use_fields()
  {
    var customer = new CustomerWithFields { FirstName = "John", LastName = "Doe", Age = 42 };
    
    Assert(customer);
  }
  
  [Fact]
  public void Exceptions_are_handled()
  {
    var customer = new CustomerThatThrows();
    
    Assert(customer);
  }
  
  [Fact]
  public void Recursion_is_handled_safely()
  {
    var parent = new TreeNode { ID = 1 };
    var child = new TreeNode { ID = 2, Parent = parent };
    parent.Children.Add(child);
    var grandchild = new TreeNode { ID = 3, Parent = child };
    child.Children.Add(grandchild);
    
    Assert(parent);
  }
  
  [Fact]
  public void Self_reference_recursion_is_handled_safely()
  {
    var item = new SelfReference() { Name = "Test"};

    item.Self = item;
    
    Assert(item);
  }
  
  [Fact]
  public void Dictionary_is_supported()
  {
    var item = new
    {
      Dict = new Dictionary<string, object>()
      {
        { "Key1", "Value1" },
        { "Key2", 42 },
        { "Key3", new Customer { FirstName = "John", LastName = "Doe", Age = 42 } }
      }
    };

    Assert(item);
  }
  
  [Fact]
  public void Enumerables_are_supported()
  {
    var item = new
    {
      List = new List<string> { "One", "Two", "Three" },
      Array = new[] { 1, 2, 3 },
      Set = new HashSet<int> { 1, 2, 3 }
    };

    Assert(item);
  }
  
  public class Customer
  {
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
  }

  public class CustomerWithFields
  {
    public string FirstName;
    public string LastName;
    public int Age;
  }

  public class CustomerThatThrows
  {
    public string FirstName => throw new Exception("FirstName is not available.");
    public string LastName => throw new Exception("LastName is not available.");
    public int Age => throw new Exception("Age is not available.");
    public AddressInfo Address => new AddressInfo();

    public class AddressInfo
    {
      public string Street => throw new Exception("Street is not available.");
      public string City => throw new Exception("City is not available.");
    }
  }

  public class TreeNode
  {
    public int ID { get; set; }
    public TreeNode? Parent { get; set; }
    public List<TreeNode> Children { get; } = new();
  }

  public class SelfReference
  {
    public string Name { get; set; }
    public SelfReference Self { get; set; }
  }
}