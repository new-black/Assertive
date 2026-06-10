using Assertive.Poc;

var x = "foobar";
var expectedLength = 5;
var y = "hello world";

Run("1. Passing assertion (intercepted; operands evaluated exactly once, delegate never invoked)",
  () => PocAssert.That(() => x.Length == 6 && y.Contains("world")));

Run("2. Failing equality, decomposed with live values from the closure",
  () => PocAssert.That(() => x.Length == expectedLength));

Run("3. Failing second clause of && — pinpoints which conjunct failed",
  () => PocAssert.That(() => x.StartsWith("foo") && y.Contains(x)));

Run("4. Comparison operator",
  () => PocAssert.That(() => x.Length < expectedLength));

Run("5. Unsupported shape (instance field access) — graceful generated fallback",
  () => new InstanceDemo().Check());

var people = new List<Person> { new("Alice", 30), new("Bob", 25) };
var noPeople = new List<Person>();

Run("6. Any with filter, no matching items (AnyPattern)",
  () => PocAssert.That(() => people.Any(p => p.Age > 40)));

Run("7. Any with filter on an empty collection (AnyPattern)",
  () => PocAssert.That(() => noPeople.Any(p => p.Age > 40)));

Run("8. Unfiltered Any on an empty collection (AnyPattern)",
  () => PocAssert.That(() => noPeople.Any()));

Run("9. Negated Any — expected no matches, but found some (AnyPattern)",
  () => PocAssert.That(() => !people.Any(p => p.Age >= 25)));

Run("10. Passing Any combined with a failing comparison via &&",
  () => PocAssert.That(() => people.Any(p => p.Name == "Alice") && people.Count == 3));

static void Run(string name, Action assertion)
{
  Console.WriteLine($"--- {name} ---");

  try
  {
    assertion();
    Console.WriteLine("PASSED");
  }
  catch (PocAssertionException ex)
  {
    Console.WriteLine(ex.Message);
  }

  Console.WriteLine();
}

class InstanceDemo
{
  private readonly int _count = 3;

  public void Check() => PocAssert.That(() => _count == 4);
}

record Person(string Name, int Age);
