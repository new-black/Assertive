using System;
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
  public void Exceptions_are_handled_in_nested_objects()
  {
    var customer = new CustomerThatThrows();
    
    Assert(customer);
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
  
}