namespace Assertive.Examples;
using static Assertive.DSL;

public class Tests
{
  public class Payment
  {
    public decimal Amount { get; set; }
  }

  [Test]
  public void Test1()
  {
    var payment = new Payment { Amount = 100m };
    Assert(() => payment.Amount == 50);
  }

  [Test]
  public void Test2()
  {
    var list = new List<string>()
    {
      "bar"
    };

    Assert(() => list.Count == 1 && list[0].StartsWith("foo"));
  }

  [Test]
  public void Test3()
  {
    string[] GetNames() => [];

    var names = GetNames();

    Assert(() => names.Single() == "Bob");
  }


  [Test]
  public void Test4()
  {
    string[] GetNames() => ["Bob", "Alice", "John"];

    var names = GetNames();

    Assert(() => names.Single() == "Bob");
  }

  public class Order
  {
    public string ID { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal TotalAmount { get; set; }
  }

  [Test]
  public void Test5()
  {
    var orders = Enumerable.Range(0, 100).Select(i =>
    {
      var amount = Random.Shared.NextInt64(12500, 20000) / 100.0m;
      return new Order
      {
        ID = $"#{i}",
        PaidAmount = amount,
        TotalAmount = amount
      };
    }).ToList();

    orders[29].PaidAmount = 0;

    Assert(() => orders.All(o => o.PaidAmount > 100));
  }

  class Customer
  {
    public int ID { get; set; }
    public string FirstName { get; set; }
  }

  [Test]
  public void Test6()
  {
    var customers = new List<Customer> { 
      new Customer { ID = 1, FirstName = "Bob" },
      new Customer { ID = 2, FirstName = "John" },
      new Customer { ID = 3, FirstName = "Alice" }
    };

    var expectedCount = 2;

    Assert(() => customers.Count == expectedCount);
  }
  
  [Test]
  public void Test7()
  {
    int a = 20;
    int b = 24;
    
    Assert(() => a == b);
  }
}