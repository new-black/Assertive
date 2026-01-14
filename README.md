# About Assertive

Assertive is a free, open source library available on [NuGet](https://www.nuget.org/packages/Assertive/) for easily writing test assertions using the power of the C# language and aims to be the easiest possible way to write assertions while still providing useful and contextual error information. It's not a test framework of itself, it's meant to be used in conjunction with a test framework like xUnit, NUnit, TUnit or MSTest.

```csharp
Assert(() => order.Status == OrderStatus.Paid && order.Items.All(i => i.Quantity > 0));
```

Assertive does away with a long list of possible assertion methods or "fluent" assertion chaining and only provides a single `Assert.That` method (or just `Assert()` if you add `using static Assertive.DSL`).

## Installation

```
dotnet add package Assertive
```

Or add to your project file:

```xml
<PackageReference Include="Assertive" Version="0.16.0" />
```

For snapshot testing with xUnit, also add:

```
dotnet add package Assertive.xUnit
```

## Contents

- [Installation](#installation)
- [What it looks like](#what-it-looks-like)
- [Using the DSL](#using-the-dsl)
- [How is this different from using Assert.IsTrue?](#how-is-this-different-from-using-assertistrue)
- [Features](#features)
  - [Multiple assertions](#multiple-assertions)
  - [Exception assertions](#exception-assertions)
  - [Snapshot testing](#snapshot-testing)
    - [Configuration](#configuration)
    - [Expected files](#expected-files)
    - [Normalization](#normalization)
    - [Placeholders](#placeholders)
    - [Advanced configuration](#advanced-configuration)
  - [Exception handling](#exception-handling)
    - [NullReferenceExceptions](#nullreferenceexceptions)
    - [IndexOutOfRangeException](#indexoutofrangeexception)
    - [InvalidOperationException caused by Single/First](#invalidoperationexception-caused-by-singlefirst)
    - [KeyNotFoundException](#keynotfoundexception)
    - [InvalidCastException](#invalidcastexception)
    - [FormatException](#formatexception)
    - [DivideByZeroException](#dividebyzeroexception)
    - [ArgumentOutOfRangeException](#argumentoutofrangeexception)
  - [Custom messages](#custom-messages)
  - [Analysis of each item when collection.All fails](#analysis-of-each-item-when-collectionall-fails)
  - [Contents of locals used in your assertion are rendered to the output](#contents-of-locals-used-in-your-assertion-are-rendered-to-the-output)
  - [Custom patterns](#custom-patterns)
    - [Pattern matching](#pattern-matching)
    - [Template placeholders](#template-placeholders)
    - [Negation](#negation)
- [Colors](#colors)
  - [When colors are enabled](#when-colors-are-enabled)
  - [When colors are disabled](#when-colors-are-disabled)
  - [Configuration](#configuration-1)
- [Output](#output)
  - [Value length truncation](#value-length-truncation)
- [Compatibility](#compatibility)
  - [.NET](#net)
  - [Test frameworks](#test-frameworks)
- [Limitations](#limitations)

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

<img width="646" height="218" alt="image" src="https://github.com/user-attachments/assets/0511b1fc-643b-45bc-b70b-9596d141fb4c" />

## Using the DSL

There are two ways to write assertions with Assertive:

```csharp
// Using the Assert class directly
Assert.That(() => payment.Amount == 50);

// Using the DSL for a more concise syntax
using static Assertive.DSL;

Assert(() => payment.Amount == 50);
```

The `using static Assertive.DSL` import allows you to write `Assert()` instead of `Assert.That()`. Both are functionally identical, so use whichever style you prefer.

## How is this different from using Assert.IsTrue?

While `Assert.IsTrue(a == b)` would have the same result for a passing test, it will give you an opaque error message about false not being true or something along those lines when the test fails. 

Because Assertive accepts an expression it will analyze the expression and output an error message like this:

<img width="642" height="181" alt="image" src="https://github.com/user-attachments/assets/324a8471-d4c5-4c71-b70b-b1b493c7832a" />

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

You can also define your own [custom patterns](#custom-patterns).

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

<img width="658" height="220" alt="image" src="https://github.com/user-attachments/assets/29fb06cf-e1f2-48c6-814e-cb2978da2954" />

Short-circuiting works as you would expect, if the first assertion fails then the second one is not evaluated.

Likewise, it's possible to use a bitwise AND (`&`) to force evaluation of both sides.

### Exception assertions

To assert that code throws an exception, use `Assert.Throws`:

```csharp
// Assert that any exception is thrown
var ex = Assert.Throws(() => SomeMethodThatThrows());

// Assert that a specific exception type is thrown
var ex = Assert.Throws<ArgumentException>(() => Validate(null));

// Assert exception type and validate the exception
var ex = Assert.Throws<ArgumentException>(
    () => Validate(null),
    e => e.ParamName == "input"
);
```

The thrown exception is returned, so you can perform additional assertions on it:

```csharp
var ex = Assert.Throws<InvalidOperationException>(() => Process());
Assert(() => ex.Message.Contains("not initialized"));
```

For async code, use the async overloads:

```csharp
var ex = await Assert.Throws<HttpRequestException>(
    async () => await client.GetAsync("https://invalid.url")
);
```

If the code doesn't throw (or throws the wrong exception type), Assertive reports exactly what happened:

```csharp
Assert.Throws<ArgumentException>(() => Validate("valid input"));
// Fails with: Expected ArgumentException but no exception was thrown.

Assert.Throws<ArgumentException>(() => ThrowsInvalidOperation());
// Fails with: Expected ArgumentException but InvalidOperationException was thrown.
```

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
Configuration.Snapshots.Normalization.NormalizeGuid = false;
Configuration.Snapshots.Normalization.NormalizeDateTime = false;
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
Configuration.Snapshots.Normalization
  .RegisterPlaceholderValidator("price", value => decimal.TryParse(value, out var price) && price > 0, "Price must be a positive number.");
```

#### Advanced configuration

Several additional options are available for fine-tuning snapshot behavior:

**Ignoring properties:**

```csharp
Configuration.Snapshots.ShouldIgnore = (property, obj, value) =>
{
    // Ignore all properties ending with "Id"
    return property.Name.EndsWith("Id");
};
```

**Handling extraneous properties:**

When the actual object has properties that don't exist in the expected snapshot, you can control the behavior:

```csharp
// Fail the test (default)
Configuration.Snapshots.ExtraneousProperties = (name, value) => ExtraneousPropertiesOptions.Disallow;

// Ignore extra properties
Configuration.Snapshots.ExtraneousProperties = (name, value) => ExtraneousPropertiesOptions.Ignore;

// Auto-update the expected file with new properties
Configuration.Snapshots.ExtraneousProperties = (name, value) => ExtraneousPropertiesOptions.AutomaticUpdate;
```

**Custom exception rendering:**

When a property getter throws an exception during serialization:

```csharp
Configuration.Snapshots.ExceptionRenderer = (property, obj, exception) =>
{
    return $"<{exception.GetType().Name}>";
};
```

**Custom expected file location:**

```csharp
Configuration.Snapshots.ExpectedFileDirectoryResolver = (testMethod, sourceFile) =>
{
    // Store all snapshots in a central __snapshots__ folder
    return Path.Combine(sourceFile.DirectoryName!, "__snapshots__");
};
```

**Bulk snapshot regeneration:**

When you need to regenerate all expected files (e.g., after a major refactoring):

```csharp
Configuration.Snapshots.TreatAllSnapshotsAsCorrect = true;
```

This will overwrite ALL expected files with actual values - both new and existing. Remember to set it back to `false` after regenerating.

**Auto-accepting new snapshots only:**

When writing new tests (especially useful for AI agents and automated workflows), you can auto-accept new snapshots while still failing on changes to existing ones:

```csharp
Configuration.Snapshots.AcceptNewSnapshots = true;
```

Unlike `TreatAllSnapshotsAsCorrect`, this only affects new snapshots where no expected file exists yet. Existing snapshots are still compared normally - changes to existing snapshots will still fail the test.

**New snapshot workflow:**

When a snapshot assertion fails because no expected file exists, Assertive:

1. Shows the expected file path in the error message
2. Shows the actual JSON value that needs to be saved
3. Creates an empty expected file (so the directory structure exists)

To accept a new snapshot, copy the actual JSON from the error message to the expected file.

### Exception handling

Assertive has special handling of certain common exceptions that occur when writing tests, providing immediate feedback on what caused the exception without having to attach the debugger or dig through stacktraces.

#### NullReferenceExceptions

When a `NullReferenceException` occurs somewhere within your assertion (because the thing you thought wasn't going to be `null` was in fact `null`) Assertive will try to find the cause of that exception by looking at what you dereferenced and what part of that was `null` or returned `null`. 

Example:

```csharp
Foo foo = new Foo();
      
Assert(() => foo.Bar.Value.Length == 1);
```

Assuming `foo.Bar` was not initialized, this assertion will fail with a message of:

> NullReferenceException caused by accessing Value on foo.Bar which was null.

Likewise, an `ArgumentNullException` caused by calling a LINQ method such as `Where` on an `IEnumerable<T>` that is `null` is handled in the same way.

Because of this, you can omit null checks in your assertions while still getting helpful error messages.

#### IndexOutOfRangeException

When an `IndexOutOfRangeException` is thrown because you access an array or list index that is out of bounds, Assertive will try to find the cause of the exception by looking at where you accessed an index that was out of bounds.

Example:

```csharp
int[] data = GetData();
      
var start = GetStartIndex();

Assert(() => data[start] > 0)
```

Assuming `data` only has a length of 2 but `GetStartIndex()` returned 4, this will fail with a message of:

> IndexOutOfRangeException caused by accessing index start (value: 4) on data, actual length was 2.

#### InvalidOperationException caused by Single/First

If you have a sequence that you thought was only going to contain one item but in fact contained multiple, or if you have a sequence that you thought was going to contain something but that was actually empty, an `InvalidOperationException` will be thrown. Assertive will try to find the cause of this exception and report the contents of the sequence.

Example:

```csharp
var names = GetNames();

Assert(() => names.Single() == "Bob");
```

Message if `names` is empty:

<img width="776" height="418" alt="image" src="https://github.com/user-attachments/assets/6fb7d74e-5330-4626-97dd-aef7e1cd2435" />

However if `names` has more than one element:

<img width="999" height="485" alt="image" src="https://github.com/user-attachments/assets/f992f5b8-8a7a-44c3-bf23-e0038c04e69a" />

#### KeyNotFoundException

When a `KeyNotFoundException` is thrown because you access a dictionary key that doesn't exist, Assertive will identify the missing key and show the available keys in the dictionary.

Example:

```csharp
var dict = new Dictionary<string, int>
{
    ["foo"] = 1,
    ["bar"] = 2
};

var key = "missing";

Assert(() => dict[key] == 3);
```

This will fail with a message of:

> KeyNotFoundException caused by accessing key key (value: "missing") on dict. Available keys: "foo", "bar".

#### InvalidCastException

When an `InvalidCastException` is thrown because of a failed explicit cast, Assertive will show both the target type and the actual type of the object.

Example:

```csharp
object obj = 42;

Assert(() => (string)obj == "42");
```

This will fail with a message of:

> InvalidCastException caused by casting obj to string. Actual type was int.

#### FormatException

When a `FormatException` is thrown by parsing methods like `int.Parse()` or `DateTime.Parse()`, Assertive will show the string that failed to parse and the expected type.

Example:

```csharp
var input = "abc";

Assert(() => int.Parse(input) == 123);
```

This will fail with a message of:

> FormatException caused by calling int.Parse("abc"). "abc" is not a valid int.

#### DivideByZeroException

When a `DivideByZeroException` is thrown because of integer division or modulo by zero, Assertive will identify which expression evaluated to zero.

Example:

```csharp
var a = 10;
var b = 0;

Assert(() => a / b == 0);
```

This will fail with a message of:

> DivideByZeroException caused by dividing a by b (value: 0).

#### ArgumentOutOfRangeException

When an `ArgumentOutOfRangeException` is thrown by methods like `string.Substring()`, Assertive will show the method call, the arguments used, and relevant context like the string length.

Example:

```csharp
var str = "hello";

Assert(() => str.Substring(10) == "world");
```

This will fail with a message of:

> ArgumentOutOfRangeException caused by calling Substring(10) on str (length: 5).

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

<img width="656" height="662" alt="image" src="https://github.com/user-attachments/assets/c7c3c85a-0e82-4724-82ce-41c57331dd46" />

### Contents of locals used in your assertion are rendered to the output

If you have an assertion like `Assert(() => customers.Count() == expectedCustomers)` that references local variables and it fails, the contents of the locals you use in your assertion are rendered to the test output.

For example:

<img width="871" height="221" alt="image" src="https://github.com/user-attachments/assets/19d5f4b8-28a8-4e57-82ad-762851522cc9" />

But note how only `customers` is rendered as the value of `expectedCustomers` is already displayed in the message at some other point.

### Custom patterns

If you have custom extension methods or properties that you use frequently in your assertions, you can register custom patterns to provide friendly error messages for them.

For example, say you have a `None()` extension method that checks if a collection is empty:

```csharp
public static bool None<T>(this IEnumerable<T> source) => !source.Any();
```

You can register a pattern for it like this:

```csharp
Configuration.Patterns.Register("None", new PatternDefinition
{
    Match = [new MatchPredicate { Method = new MethodMatch { Name = "None" } }],
    AllowNegation = true,
    Output = new OutputDefinition
    {
        Expected = "Collection {instance} should not contain any items.",
        Actual = "It contained {instance.count} items."
    },
    OutputWhenNegated = new OutputDefinition
    {
        Expected = "Collection {instance} should contain at least one item.",
        Actual = "It was empty."
    }
});
```

The first parameter is a unique name for the pattern. If you register a pattern with a name that already exists, the new pattern replaces the old one.

You can also remove a pattern by name:

```csharp
Configuration.Patterns.Unregister("None");
```

Or remove all custom patterns:

```csharp
Configuration.Patterns.Clear();
```

Now when `Assert(() => list.None())` fails, you'll get a message like:

> Collection list should not contain any items. It contained 3 items.

And when `Assert(() => !list.None())` fails:

> Collection list should contain at least one item. It was empty.

#### Pattern matching

The `Match` array contains predicates that must all match (AND logic). Available predicates:

**Method matching:**
- `Method.Name` - matches the method name exactly
- `Method.ParameterCount` - matches methods with this exact parameter count
- `Method.IsExtension` - matches only extension methods (true) or non-extension methods (false)

**Property matching:**
- `Property.Name` - matches a property access by name

**Type and namespace:**
- `DeclaringType` - matches the type that declares the method or property (by name or full name)
- `Namespace` - matches the namespace of the declaring type
- `InstanceType` - matches the type of the instance (supports generic types like `List`)

#### Template placeholders

Output templates support the following placeholders:

**Instance:**
- `{instance}` - the expression the method/property was called on (e.g., `list` in `list.None()`)
- `{instance.value}` - the evaluated value of the instance
- `{instance.type}` - the type name (e.g., `List<String>`)
- `{instance.count}` - the count of items if the instance is a collection
- `{instance.firstTenItems}` - the first 10 items of a collection (e.g., `[1, 2, 3] ...`)

**Method arguments:**
- `{arg0}`, `{arg1}`, etc. - argument expressions by position
- `{arg0.value}`, `{arg1.value}`, etc. - evaluated argument values
- `{arg0.type}`, `{arg1.type}`, etc. - argument type names

**Other:**
- `{method}` - the method name
- `{property}` - the property name (for property patterns)
- `{value}` - the property value (for property patterns)

#### Negation

When `AllowNegation` is `true`, the pattern will also match when the assertion is negated with `!`. Provide a separate `OutputWhenNegated` to customize the message for this case.

## Colors

Assertive uses ANSI color codes to make assertion failure messages easier to read, with syntax highlighting for C# expressions, color-coded expected/actual sections, and visual diff highlighting.

### When colors are enabled

Colors are **enabled by default** when running tests locally. This works well in most terminal-based test runners and IDEs that support ANSI codes.

### When colors are disabled

Colors are **automatically disabled** in the following situations:

- **NUnit** - NUnit's test output doesn't handle ANSI escape codes well, so colors are disabled when NUnit is detected
- **CI environments** - Colors are disabled when any of these environment variables are detected:
  - `NO_COLOR` (any value)
  - `CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`, `CIRCLECI`, `TRAVIS`, `TEAMCITY_VERSION`, `BUILDKITE`, `DRONE`, `APPVEYOR`
  - `BUILD_BUILDID`, `JENKINS_HOME`, `HUDSON_URL`, `BITBUCKET_BUILD_NUMBER`, `BITBUCKET_PIPELINE_UUID`

### Configuration

You can override the automatic detection using the `ASSERTIVE_COLORS_ENABLED` environment variable:

```bash
# Force colors on
ASSERTIVE_COLORS_ENABLED=true dotnet test

# Force colors off
ASSERTIVE_COLORS_ENABLED=false dotnet test
```

Or configure it programmatically:

```csharp
// Disable colors entirely
Configuration.Colors.Enabled = false;

// Disable only syntax highlighting (keep other colors)
Configuration.Colors.UseSyntaxHighlighting = false;
```

## Output

### Value length truncation

By default, Assertive outputs the entire serialized value of objects in Expected/Actual sections. For large objects, this can produce very long output. You can limit the output length:

```csharp
// Limit serialized values to 500 characters (values exceeding this will be truncated with "...")
Configuration.Output.MaxValueLength = 500;

// Unlimited output (default)
Configuration.Output.MaxValueLength = null;
```

## Compatibility

### .NET

Assertive targets .NET 8. 

### Test frameworks

Assertive is currently compatible with:

- xUnit
- MSTest
- NUnit
- TUnit

It will work fine with any other test framework as well, but the exception that Assertive will throw will not be recognized by those test frameworks and likely not display quite as nicely.

Snapshot testing is currently only supported on NUnit, TUnit and xUnit. On xUnit it's required to have this attribute somewhere from the Assertive.xUnit package, otherwise it won't be able to detect the currently running test:

```csharp
[assembly: EnableAssertiveSnapshots]
```

## Limitations

- Assertive is entirely based on the .NET Expression API which has some limitations in the syntax that is supported inside an expression. Most notable is a lack of support for `await`, `dynamic`, tuple literals and the `?.` operator. 
- For accurate messages on failing tests it's important that the assertions themselves are side-effect free and don't modify state, as Assertive works by evaluating expressions multiple times in case of a failed assertion. If the assertion modifies state then that state will be modified multiple times.



