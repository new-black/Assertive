# About Assertive

Assertive is a free, open source library available on [NuGet](https://www.nuget.org/packages/Assertive/) for easily writing test assertions using the power of the C# language. It's not a test framework of itself, it's meant to be used in conjunction with a test framework like xUnit or MSTest. 

Assertive does away with a long list of possible assertion methods or "fluent" assertion chaining and only provides a single `Assert.That` method (or just `Assert()` if you add `using static Assertive.DSL`).

## What it looks like

With Assertive, instead of:

`Assert.Equal(expected, actual)` 

You just write:

`Assert(() => expected == actual)`

## How is this different from using Assert.IsTrue?

While `Assert.IsTrue(a == b)` would have the same result for a passing test, it will give you an opaque error message about false not being true or something along those lines when the test fails. 

But because Assertive accepts an expression it will analyze the expression and give an error like this:

```
Expected a to equal b but a was 20 while b was 24.

Assertion: a == b
```

Assertive has a number of built-in patterns that it recognizes, which currently consists of:

- Boolean check (`Assert(() => success)`)
- Equality comparison (`Assert(() => a == b)`)
- Numerical comparisons (`Assert(() => a >= b)`)
- Null checks (`Assert(() => value != null)`)
- Size and length checks (`Assert(() => customers.Count(c => c.Age > 50) > 0))` or `Assert(() => name.Length < 50)`)
- Collection existence checks (`Assert(() => customers.Any(c => c.Age <= 40))` or `Assert(() => customers.All(c => c.IsVerified))`)
- Collection contains checks (`Assert(() => result.Contains("test"))`)
- Collection equality comparison (`Assert(() => seq1.SequenceEqual(seq2))`)

When there is no matching pattern for your assertion, it will simply report the assertion that failed.

## Other features

### Multiple assertions

It's possible to use multiple related assertions in the same statement, which makes for clearer and shorter code.

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

### Exception handling

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

> InvalidOperationException caused by calling Single on list which contains no elements.

However if `names` has more than one element:

> InvalidOperationException caused by calling Single on names which contains more than one element. Actual element count: 2.
> 
> Value of list: ["Bob", "John"]

## Test frameworks

Assertive is currently compatible with:

- xUnit
- MSTest
- NUnit 

It will work fine with any other test framework as well, but the exception that Assertive will throw will not be recognized by those test frameworks and likely not display quite as nicely.

## Limitations

- Assertive is entirely based on the .NET Expression API which has some limitations in the syntax that it supports. Most notable is a lack of support for `await`, `dynamic` and the `?.` operator. 
- For accurate messages on failing tests it's important that the assertions themselves are side-effect free and don't modify state, as Assertive works by evaluating expressions multiple times in case of a failed assertion. If the assertion modifies state then that state will modified multiple times.


