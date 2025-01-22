# About Assertive

Assertive is a free, open source library available on [NuGet](https://www.nuget.org/packages/Assertive/) for easily writing test assertions using the power of the C# language and aims to be the easiest possible way to write assertions while still providing useful and contextual error information. It's not a test framework of itself, it's meant to be used in conjunction with a test framework like xUnit or MSTest. 

Assertive does away with a long list of possible assertion methods or "fluent" assertion chaining and only provides a single `Assert.That` method (or just `Assert()` if you add `using static Assertive.DSL`).

## What it looks like

With Assertive, instead of:

```csharp
Assert.NotNull(payment);
Assert.Equal(50, payment.Amount);
```

You just write:

```csharp
Assert(() => payment != null && payment.Amount == 50);
```

Or even just:

```csharp
Assert(() => payment.Amount == 50);
```

As the null check [isn't necessary.](#nullreferenceexceptions)

If the assertion fails you will get this output in your test runner:

> Expected payment.Amount to equal 50 but payment.Amount was 51.
>
> Assertion: payment.Amount == 50
>
> Locals:
>
> - payment = { Amount = 51, Date = 2020-02-03T13:15:12.0000000, Customer = "John Doe" }

## How is this different from using Assert.IsTrue?

While `Assert.IsTrue(a == b)` would have the same result for a passing test, it will give you an opaque error message about false not being true or something along those lines when the test fails. 

Because Assertive accepts an expression it will analyze the expression and output an error message like this:

```
Expected a to equal b but a was 20 while b was 24.

Assertion: a == b
```

Assertive has a number of built-in patterns that it recognizes, which currently consists of:

- Boolean check (`Assert(() => success)`)
- Equality comparison (`Assert(() => a == b)`)
- Numerical comparisons (`Assert(() => a >= b)`)
- Null checks (`Assert(() => value != null)` or `Assert(() => creditBalance.HasValue)` or `Assert(() => value is object)`)
- Size and length checks (`Assert(() => customers.Count(c => c.Age > 50) > 0))` or `Assert(() => name.Length < 50)`)
- Collection existence checks (`Assert(() => customers.Any(c => c.Age <= 40))` or `Assert(() => customers.All(c => c.IsVerified))`)
- Collection contains checks (`Assert(() => result.Contains("test"))`)
- Collection equality comparison (`Assert(() => seq1.SequenceEqual(seq2))`)
- String contains/starts with/ends with checks (`Assert(() => name.StartsWith("John"))`)
- Reference equality checks (`Assert(() => ReferenceEquals(a, b))`)
- Type checks (`Assert(() => value is string)`)

When there is no matching pattern for your assertion, it will simply report the assertion that failed plus whatever useful information can be distilled from the assertion.

## Features

### Multiple assertions

It's possible to write multiple related assertions in the same statement, which makes for clearer and shorter code.

For example: 

```csharp
var list = new List<string>()
{
    "bar"
};

Assert(() => list.Count == 1 && list[0].StartsWith("foo"));

```

This assertion would fail with the message:

> Expected list[0] to start with "foo".
>
> Value of list[0]: "bar"
>
> Assertion: list[0].StartsWith("foo")

Short-circuiting works as you would expect, if the first assertion fails then the second one is not evaluated.

Likewise, it's possible to use a bitwise AND (`&`) to force evaluation of both sides. 

### Snapshot testing

Inspired by the snapshot testing of [Verify](https://github.com/VerifyTests/Verify) Assertive also supports snapshot testing of objects. What this means is that you simply call `Assert(myObject);` (or `Assert.Snapshot` if not using `using static Assertive.DSL`) and a snapshot is made of the object (in JSON format) and is compared to a stored
snapshot from a previous execution. If they still match, the test passes and otherwise it fails. 

The first time you add a snapshot assertion, no `expected.json` file exists yet for the assertion. If you have a diff tool like WinMerge installed, you can integrate with the excellent [DiffEngine](https://github.com/VerifyTests/DiffEngine) and register it like this:

```csharp
Configuration.Snapshots.LaunchDiffTool = (actual, expected) =>
{
    DiffRunner.Launch(actual, expected);
};
```

A window will pop up with the actual file and the expected file (or an empty one if one doesn't exist yet) and if the `actual.json` file matches your expectations, you can copy the contents of the `actual.json` file to `expected.json` and commit it to version control.

#### Configuration

You can change the global configuration of snapshot testing with the `Assertive.Config.Configuration.Snapshots` property. 

You can also create a test specific copy and pass it to `Assert`:

```csharp
Assert(obj, Configuration.Snapshots with { ExcludeNullValues = true });
```

#### Expected files

By default, the expected file is created in the same directory as the test itself and has this format:

`<Class>.<Test>#<Expression>_<Counter>.expected.json`

So when you have this test:

```csharp
public class CustomerTests
{
    [Fact]
    public async Task GetCustomer_works()
    {
        var fetchedCustomer = await GetCustomer(1);
        Assert(fetchedCustomer);
    }
}
```

The file name will end up being:

`CustomerTests.GetCustomer_works#fetchedCustomer_1.expected.json`

If you have multiple assertions on the same `fetchedCustomer` (if you make any modifications for example) then a `_2` file will be created and so on.

In case you want to modify this, you can alter the `<Expression>` part like so:

```csharp
Assert(fetchedCustomer, "myAssertion");
```

And the file created will be:

`CustomerTests.GetCustomer_works#myAssertion.expected.json`

#### Normalization

Normalization can be used to change volatile values such as identifiers, Guids, dates and times into constant ones. By default, `Guid` and various built-in date types are normalized. So in the output snapshot, a Guid like `"c8f33fe6-a30e-42ed-8283-b51a5eced158"` will simply become `"{Guid}"`. 

This can be changed with:

```csharp
Configration.Snapshots.Normalization.NormalizeGuid = false;
Configration.Snapshots.Normalization.NormalizeDateTime = false;
```

More advanced configuration is also possible:

```csharp
Configuration.Snapshots.Normalization.ValueRenderer = (property, obj, value) =>
{
    if (property.Name.Contains("Id"))
    {
        return "{Id}";
    }

    return value;
};
```

This approach lets you easily stabilize test outputs.

#### Placeholders

Instead of normalizing the `actual.json`, you can also work with placeholders in the `expected.json` (or any mix of the two). 

Say you have this `actual.json`:

```json
{
    "ProductId" : "8731580994818888942",
    "Price" : 34.99
}
```

Assuming both are volatile (so changing every test run) you could add a normalizer for all `decimal` properties, or add a very specific exclusion for this test, or you could define the `expected.json` like this:

```json
{
    "ProductId" : "@@productid",
    "Price" : "@@price" 
}
```

Anything after `@@` is arbitrary and up to you. As long as `actual` and `expected` both have the property, it will be considered a match. You can configure the placeholder prefix with `Configuration.Snapshots.Normalization.PlaceholderPrefix` and it defaults to `"@@"`. 

##### Advanced placeholders 

Say you have this snapshot:

```json
{
    "ProductId" : "8731580994818888942",
    "BeforePrice" : 34.99,
    "AfterPrice" : 34.99,
    "TaxAmount" : 3.50,
    "DiscountAmount" : 0
}
```

In this imagined scenario, it's important that BeforePrice and AfterPrice are the same, simply replacing both with a constant placeholder would not be good enough. To support this, you can tag each distinct value with a number to indicate two of the same placeholder type should have the same value:

```json
{
    "ProductId" : "@@productid",
    "BeforePrice" : "@@price#1", 
    "AfterPrice" : "@@price#1",
    "TaxAmount" : "@@price#2",
    "DiscountAmount" : 0
}
```

To go even further, you can register a validator for each placeholder type:

```csharp
Configuration.Snapshots.Normalization.RegisterPlaceholderValidator("price", value => decimal.TryParse(value, out var price) && price > 0, "Price must be a positive number.");
```


### Exception handling

Assertive has special handling of certain common exceptions that occur when writing tests, providing immediate feedback on what caused the exception without having to attach the debugger or dig through stacktraces.

#### NullReferenceExceptions

When a NullReferenceException occurs somewhere within your assertion (because the thing you thought wasn't going to be `null` was in fact `null`) Assertive will try to find the cause of that exception by looking at what you dereferenced and what part of that was `null` or returned `null`. 

Example:

```csharp
Foo foo = new Foo();
      
Assert(() => foo.Bar.Value.Length == 1);
```

Assuming foo.Bar was not initialized, this assertion will fail with a message of:

> NullReferenceException caused by accessing Value on foo.Bar which was null.

Likewise, an `ArgumentNullException` caused by calling a LINQ method such as `Where` on an `IEnumerable<T>` that is `null` is handled in the same way.

Because of this, you can omit null checks in your assertions while still getting helpful error messages.

#### IndexOutOfRangeException

When an IndexOutOfRangeException is thrown because you access an array or list index that is out of bounds, Assertive will try to find the cause of the exception by looking at where you accessed an index that was out of bounds.

Example:

```csharp
int[] data = GetData();
      
var start = GetStartIndex();

Assert(() => data[start] > 0)
```

Assuming `data` only has a length of 2 but `GetStartIndex()` returned 4, this will fail with a message of:

> IndexOutOfRangeException caused by accessing index start (value: 4) on data, actual length was 2.

#### InvalidOperationException caused by Single/First

If you have a sequence that you thought was only going to contain one item but in fact contained multiple, or if you have a sequence that you thought was going to contain something but that was actually empty, an InvalidOperationException will be thrown. Assertive will try to find the cause of this exception and report the contents of the sequence.

Example:

```csharp
var names = GetNames();

Assert(() => names.Single(n => n == "Bob"))
```

Message if `names` is empty:

> InvalidOperationException caused by calling Single on names which contains no elements.

However if `names` has more than one element:

> InvalidOperationException caused by calling Single on names which contains more than one element. Actual element count: 2.
> 
> Value of names: ["Bob", "John"]

### Custom messages

In case you want to add more context to your assertion, or to document the intent of the assertion more clearly, Assertive offers an overload that lets you supply your own message that will be printed if the assertion fails.

Example:

```csharp
Assert(() => order.Amount < 100, "Expected the discount on the order to reduce the amount to below 100");
```

Additionally, instead of a string, any object can be provided to provide more context:

```csharp
Assert(() => order.Amount < 100, order);
```

This would print the contents of the `order` object as JSON-like:

```
{
    Amount: 120,
    Discount: -15
}
```

Another overload exists that allows you to provide the context object as an expression, for example:

```csharp
Assert(() => order.Amount < 100, () => orderID);
```

Which will output:

```
Context: orderID = 10
```

### Analysis of each item when `collection.All` fails

When you have a collection of items and you want to check each item, instead of writing a `foreach` on the collection, you can simply write `collection.All(<assertion>)`. 

For example:

```csharp
Assert(() => orders.All(o => o.PaidAmount > 100));
```

Assuming the 29th order (starting from zero) in this collection did not meet this condition, the message will be something like this:

> Expected all items of orders to match the filter o.PaidAmount > 100, but this item did not: { ID = 54, PaidAmount = 50 }
>
> orders[29] - Expected item.PaidAmount to be greater than 100, but item.PaidAmount was 50.

### Contents of locals used in your assertion are rendered to the output

If you have an assertion like `Assert(() => customers.Count() == expectedCustomers)` that references local variables and it fails, the contents of the locals you use in your assertion are rendered to the test output. 

For example:

> Expected customers to have a count equal to expectedCustomers (value: 2) but the actual count was 3.
>
> Assertion: customers.Count() == expectedCustomers
>
> Locals:
>
> - customers = [ { ID = 1, FirstName = "John" }, { ID = 2, FirstName = "Bob" }, { ID = 3, FirstName = "Alice " } ]

But note how only `customers` is rendered as the value of `expectedCustomers` is already displayed in the message at some other point.

## Compatibility

### .NET

Assertive targets .NET 8. 

### Test frameworks

Assertive is currently compatible with:

- xUnit
- MSTest
- NUnit 

It will work fine with any other test framework as well, but the exception that Assertive will throw will not be recognized by those test frameworks and likely not display quite as nicely.

Snapshot testing is currently only supported on NUnit and xUnit. On xUnit it's required to have this attribute somewhere, otherwise it won't be able to detect the currently running test:

```csharp
[assembly: EnableAssertiveSnapshots]
```

## Limitations

- Assertive is entirely based on the .NET Expression API which has some limitations in the syntax that is supported inside an expression. Most notable is a lack of support for `await`, `dynamic`, tuple literals and the `?.` operator. 
- For accurate messages on failing tests it's important that the assertions themselves are side-effect free and don't modify state, as Assertive works by evaluating expressions multiple times in case of a failed assertion. If the assertion modifies state then that state will be modified multiple times.



