# About Assertive.Mocking

Assertive.Mocking is a free, open source library available on [NuGet](https://www.nuget.org/packages/Assertive.Mocking/) that adds source-generated, AOT-safe mocks to Assertive. Like the assertion library, it is meant to be used together with a test framework such as xUnit, NUnit, TUnit or MSTest.

```csharp
var repo = A<IOrderRepository>();

repo.GetById(123).Returns(new Order { Id = 123 });

// Build constructs an instance of a concrete type with 
// mocked values for its constructor arguments and allows specific overrides. 
var service = Build<OrderService>(repo);

var order = service.GetOrder(123);

Assert(() => order.Id == 123);
```

```csharp
// After the test has exercised the service, verify the interaction
Received(() => repo.GetById(123));
```

`A<T>()` creates a source-generated mock of `T`; `Build<T>` constructs a concrete type with auto-mocked dependencies.

## Installation

```bash
dotnet add package Assertive.Mocking
```

Or add to your project file:

```xml
<PackageReference Include="Assertive.Mocking" Version="0.1.11" />
```

The package ships a `buildTransitive` props file that automatically enables the `InterceptorsNamespaces` and `InterceptorsPreviewNamespaces` compiler features — no manual project configuration is needed.

Like Assertive itself, mocking is produced by a source generator that relies on C# interceptors, so you need to build with the **.NET 9.0.300 SDK or newer** (or any .NET 10 SDK / Visual Studio 2022 17.14+). If the generator does not run — for example because the SDK is too old — `A<T>()`/`Build<T>()` throw at runtime rather than silently degrading. The `2.Times` extension property additionally requires a **C# 14 compiler** (.NET 10 SDK); everything else works on the same toolchain as Assertive.

## Contents

- [About Assertive.Mocking](#about-assertivemocking)
- [Installation](#installation)
- [What it looks like](#what-it-looks-like)
- [Using the DSL](#using-the-dsl)
- [Arranging behavior](#arranging-behavior)
  - [Arrange inside the creation callback](#arrange-inside-the-creation-callback)
  - [Arrange after creation](#arrange-after-creation)
  - [Async arrange](#async-arrange)
  - [Strict mode](#strict-mode)
- [Returning values, throwing and side-effects](#returning-values-throwing-and-side-effects)
- [Matching arguments](#matching-arguments)
- [Verifying calls](#verifying-calls)
  - [Received and DidNotReceive](#received-and-didnotreceive)
  - [Call counts](#call-counts)
  - [No unexpected calls](#no-unexpected-calls)
  - [Call order](#call-order)
- [Capturing arguments](#capturing-arguments)
- [Properties, indexers and events](#properties-indexers-and-events)
- [Arranging an entire method at once](#arranging-an-entire-method-at-once)
- [Sequential returns and conditional setups](#sequential-returns-and-conditional-setups)
- [Class mocks, auto-mocks, builders and spies](#class-mocks-auto-mocks-builders-and-spies)
  - [Class mocking](#class-mocking)
  - [Auto-mock](#auto-mock)
  - [Building the system under test](#building-the-system-under-test)
  - [Post-creation setup with `Setup<T>`](#post-creation-setup-with-setupt)
  - [Spying with Wrap](#spying-with-wrap)
- [Resetting state](#resetting-state)
- [Compatibility](#compatibility)
- [How it works](#how-it-works)
- [Limitations](#limitations)

## What it looks like

The two styles are the same API used in different places. You can arrange behavior while creating the mock:

```csharp
var repo = A<IOrderRepository>(m =>
{
    m.GetById(123).Returns(new Order { Id = 123 });
});
```

Or you can create the mock first and arrange it later — even after `await`ing:

```csharp
var repo = A<IOrderRepository>();

repo.GetById(Any<int>()).Returns((int id) => new Order { Id = id });

await Task.Delay(1);

repo.Save(Any<Order>()).Returns(true);
```

Both produce a mock that behaves the same way during the test.

## Using the DSL

Import the static DSL class so you can call the mocking helpers directly:

```csharp
using static Assertive.Mocking.Mock;
```

You can also access all methods through the `Mock` class itself:

```csharp
var repo = Mock.A<IOrderRepository>();
```

The examples below assume the following imports: `using Assertive.Mocking;` for the types (`Times`, `Capture<T>`, `MockMode`) and arrangement extensions (`.Returns`, `.Throws`, `.Does`), `using static Assertive.Mocking.Mock;` for `A<T>()` and the matchers, and `using static Assertive.DSL;` for `Assert(...)`.

## Arranging behavior

`Arranging` is the act of telling a mock what to return, throw or do for a given call. Arrange scopes nest: creating and arranging another mock inside an arrange lambda does not disturb the outer scope.

### Arrange inside the creation callback

Pass an `Action<T>` (or `Func<T, Task>`) to `A<T>()` to configure the mock as part of its creation. This is the most compact form and matches the Assertive lambda-first style:

```csharp
var repo = A<IOrderRepository>(m =>
{
    m.GetById(123).Returns(new Order { Id = 123 });
    m.Save(default!).Returns(true);
});
```

Everything inside the callback is recorded as an arrangement. When the callback returns, the mock is ready to use.

### Arrange after creation

You can also create a mock with no arrangements and configure it later. This is useful when setup is conditional, spread across helper methods, or needs to happen after `await`ing:

```csharp
var repo = A<IOrderRepository>();

repo.GetById(123).Returns(new Order { Id = 123 });
repo.Save(default!).Returns(true);
```

Each call in the fluent chain — `repo.GetById(123).Returns(...)` — records an arrangement. The mock itself is the same object, so either style can be used.

### Async arrange

If the arrange logic is asynchronous, use the async overload of `A<T>()`:

```csharp
var repo = await A<IOrderRepository>(async m =>
{
    var template = await LoadOrderTemplateAsync();

    m.GetById(Any<int>()).Returns((int id) => template.WithId(id));
});
```

The arrange context flows across `await`, so you can mix awaited work with standalone arrange calls after creation as well.

### Strict mode

By default, mocks are loose: unarranged calls that return a mockable interface or class produce a stable auto-mock (BCL values such as strings, collections and tasks return their normal defaults). In strict mode every call must have an arrangement or a `StrictMockException` is thrown:

```csharp
var repo = A<IOrderRepository>(MockMode.Strict);

repo.GetById(123).Returns(new Order());
```

You can also pass strict mode to the arrange callback overload:

```csharp
var repo = A<IOrderRepository>(m =>
{
    m.GetById(123).Returns(new Order());
}, MockMode.Strict);
```

## Returning values, throwing and side-effects

The main arrangement verbs are `Returns`, `Throws` and `Does`, plus `ReturnsSequentially` and `ReturnsWithOuts`. For members that return `Task<T>` or `ValueTask<T>`, `.Returns(value)` takes the bare `T` and wraps it for you.

```csharp
var repo = A<IOrderRepository>();

// Return a fixed value
repo.GetById(123).Returns(new Order { Id = 123 });

// Return a computed value based on the arguments
repo.GetById(Any<int>()).Returns((int id) => new Order { Id = id });

// Same thing, expressed with method-group arrangement
Any(repo.GetById).Returns(id => new Order { Id = id });

// Throw an exception
repo.GetById(0).Throws(new InvalidOperationException("invalid id"));

// Run a side effect and return a value
repo.GetById(Any<int>()).Does(args =>
{
    var id = (int)args[0]!;
    Log.Info($"Requested order {id}");
    return new Order { Id = id };
});
```

When several arrangements match the same call, the **last registered wins**. This makes it easy to set up broad defaults first and override them with specific cases later.

For `void` methods, use `When(...)` instead of chaining on the call directly:

```csharp
var bus = A<IEventBus>();

When(() => bus.Publish(default!)).Throws(new InvalidOperationException("offline"));

When(() => bus.Publish(default!)).Does(args =>
{
    var evt = (IEvent)args[0]!;
    log.Add(evt);
});
```

## Matching arguments

By default, arguments must match exactly (structurally, for collections). Use matchers to relax that:

```csharp
var repo = A<IOrderRepository>();

// Match any value for this argument
repo.GetById(default).Returns(new Order());
repo.GetById(Any<int>()).Returns(new Order());

// Match with a predicate
repo.GetById(Any<int>(id => id > 0)).Returns(new Order());

// Match specific sets or ranges
repo.GetById(IsIn(1, 2, 3)).Returns(new Order());
repo.GetById(IsInRange(1, 100)).Returns(new Order());

// Match a non-null argument
repo.Save(IsNotNull<Order>()).Returns(true);

// Match a collection containing or being empty
repo.ProcessOrders(Contains(order1)).Returns(true);
repo.ProcessOrders(IsEmpty<Order>()).Returns(true);
```

`default` is a convenient shorthand that is treated specially which tells the generator "match any value here". It is equivalent to `Any<T>()` but does not require an explicit type. Named arguments at matcher call sites are not supported (the compiler reports `MOCK004`).

## Verifying calls

### Received and DidNotReceive

Verify that a method was called, or that it was not:

```csharp
// With using static Assertive.Mocking.Mock;
Received(() => repo.GetById(123));
DidNotReceive(() => repo.Delete(123));

// Or through the Mock class directly
Mock.Received(() => repo.GetById(123));
Mock.DidNotReceive(() => repo.Delete(123));
```

Arguments are matched structurally. A collection passed to `Received` will match a call with a logically equal collection, not just the same instance. The lambda must invoke a single mock member; calling more than one throws with a clear message.

### Call counts

Use `Times` to verify the number of matching calls:

```csharp
Received(() => repo.GetById(123), Times.Exactly(2));
DidNotReceive(() => repo.Delete(123));                    // same as Received(..., Times.Never)
Received(() => repo.Save(default!), Times.AtLeastOnce);
```

You can also use the `int` extension property for exact counts:

```csharp
Received(() => repo.GetById(123), 2.Times);
```

Available values: `Times.Once`, `Times.Never`, `Times.AtLeastOnce`, `Times.AtMostOnce`, `Times.Exactly(n)`, `Times.AtLeast(n)`, `Times.AtMost(n)`, `Times.Between(min, max)`. The `2.Times` extension property requires a C# 14 compiler; `Times.Exactly(2)` works on all supported toolchains.

### No unexpected calls

`Mock.VerifyNoOtherCalls(mock)` asserts that every recorded call matched at least one setup. Run your `Received` checks first, then this:

```csharp
Mock.VerifyNoOtherCalls(repo);
```

### Call order

`Mock.InOrder` checks that calls occurred in the specified order. Other calls may be interleaved; the listed calls only need to appear in order:

```csharp
Mock.InOrder(repo, r =>
{
    r.BeginTransaction();
    r.Save(default!);
    r.Commit();
});
```

## Capturing arguments

Use `Capture<T>` to record the arguments a method was called with and inspect them later:

```csharp
var captured = new Capture<Order>();

repo.Save(Any<Order>(captured)).Returns(true);

sut.ProcessOrder(new Order { Id = 7 });

Assert(() => captured.Latest.Id == 7);
// captured.Values — every captured Order
```

## Properties, indexers and events

Properties are arranged and verified like methods:

```csharp
var svc = A<INamedService>(s =>
{
    s.Name.Returns("Alice");
});

Assert(() => svc.Name == "Alice");

// Verify property access
Received(() => _ = svc.Name);

// Verify property assignment
svc.Name = "Bob";
Received(() => svc.Name = "Bob");
```

Indexers follow the same pattern:

```csharp
var cache = A<ICache>();

cache["key"].Returns("value");

Assert(() => cache["key"] == "value");

cache["key"] = "newValue";
Received(() => cache["key"] = "newValue");
```

Raise events with `Mock.Raise`:

```csharp
var source = A<IEventSource>();
string? message = null;
source.MessageReceived += (sender, msg) => message = msg;

Mock.Raise(source, s => s.MessageReceived += null, null, "hello");

Assert(() => message == "hello");
```

## Arranging an entire method at once

`Any` arranges every call to a method regardless of its arguments. This is convenient for default behaviors and method-group style setup:

```csharp
var repo = A<IOrderRepository>();

// Any call to GetById returns this value
Any(repo.GetById).Returns(new Order());

// Or compute from the argument
Any(repo.GetById).Returns((int id) => new Order { Id = id });

// Sequential returns
Any(repo.GetById).ReturnsSequentially(
    new Order { Id = 1 },
    new Order { Id = 2 });

// Void methods
var bus = A<IEventBus>();
Any(bus.Publish).Does((IEvent e) => log.Add(e));
```

## Sequential returns and conditional setups

`ReturnsSequentially` returns values in order; the last value is repeated once the sequence is exhausted:

```csharp
repo.GetById(default).ReturnsSequentially(
    new Order { Id = 1 },
    new Order { Id = 2 },
    new Order { Id = 3 });
```

`when` makes a setup conditional:

```csharp
bool paused = false;

repo.GetById(default).Returns(new Order(), when: () => !paused);
```

When the condition is false, the setup is skipped and the next matching setup is used.

## Class mocks, auto-mocks, builders and spies

### Class mocking

Concrete classes with virtual or abstract members can be mocked. Non-virtual members always run the real implementation. Virtual methods and gettable properties that are not arranged call the base implementation; virtual property/indexer setters and events are recorded but do not call the base implementation. Classes with `required` members and `init`-only accessors are supported.

```csharp
// Default constructor
var svc = A<MyService>();

// Forward constructor arguments
var svcWithArgs = A<MyService>("connection-string", 30);

// Arrange a virtual method
svc.ComputeHash(default!).Returns("fake-hash");
```

### Auto-mock

In loose mode, calls that return a mockable interface or class and have no arrangement return a stable child mock. `Task<T>` and `ValueTask<T>` are unwrapped automatically.

```csharp
var factory = A<IOrderServiceFactory>();

// Each unique argument gets its own stable mock
var repo = factory.GetRepository("orders");
var sameRepo = factory.GetRepository("orders");
Assert(() => ReferenceEquals(repo, sameRepo));
```

### Building the system under test

`Build<T>` constructs a class using its richest accessible constructor. Provided arguments are matched to constructor parameters by type, not by position; unmatched parameters are auto-mocked. Up to 8 arguments can be provided. Matching is by exact type — implicit conversions are not applied — but `null` and `default` are passed through to the first matching reference-type or nullable parameter.

```csharp
// All dependencies auto-mocked
var svc = Build<CheckoutService>();

// Provide specific deps; the rest are auto-mocked
var gateway = A<IPaymentGateway>();
var svcWithGateway = Build<CheckoutService>(gateway);
```

### Post-creation setup with `Setup<T>`

When a mock was created outside an `A<T>(arrange)` lambda — for example after `Wrap<T>` — use `Setup<T>` to add arrangements later:

```csharp
var repo = A<IOrderRepository>();

Setup(repo, r =>
{
    r.GetById(123).Returns(new Order());
    When(() => r.Submit(0)).Throws(new InvalidOperationException());
});
```

### Spying with Wrap

`Wrap<T>` wraps a real implementation. Calls delegate to the real object by default, but every call is recorded and specific calls can be overridden with `Setup`.

```csharp
var spy = Wrap<ITransformer>(new UpperCaseTransformer());

Assert(() => spy.Transform("hello") == "HELLO");
Received(() => spy.Transform("hello"));

Setup(spy, s => s.Transform("hello").Returns("OVERRIDDEN"));
Assert(() => spy.Transform("hello") == "OVERRIDDEN");
```

`Wrap<T>` currently supports non-generic interface types only; using a class or generic interface compiles but throws at runtime.

## Resetting state

```csharp
// Clear recorded calls but keep setups
Mock.ClearReceivedCalls(repo);

// Clear both calls and setups (also drops cached auto-mocks)
Mock.Reset(repo);
```

## Compatibility

- Targets **.NET 8 or newer**; build with the **.NET 9.0.300 SDK or newer** (see [Installation](#installation)).
- Works with xUnit, NUnit, TUnit, MSTest and any framework that renders `Assertive` failures.
- Native AOT and trimming are supported for the generated mocks; see [How it works](#how-it-works).
- The `2.Times` extension property requires a **C# 14 compiler**; `Times.Exactly(n)` works everywhere.

## How it works

When you install the package, a Roslyn incremental source generator scans your compilation for calls to `A<T>()`, `Wrap<T>()`, `Build<T>()`, and matcher-bearing mock call sites. For each mocked type it emits a concrete subclass (for classes) or interface implementation (for interfaces) that overrides every eligible member. Each generated member records the call in the mock's internal log, checks for matching setups, and either returns the configured value, invokes the base, or returns a sensible default.

`Wrap<T>` generates a similar class but holds a reference to the wrapped instance and calls through to it when no arrangement matches.

`Build<T>` is intercepted per call site: the generator reads the declared types of each argument from the semantic model, matches them to constructor parameters by type, and emits a typed factory that auto-mocks any unmatched parameters.

Argument matchers are recognised syntactically at the call site. The generator emits a companion interceptor for each matching call that registers the matcher predicates before the actual call is recorded, so the runtime never has to guess which positions were matchers.

The active arrange scope is tracked in `AsyncLocal<T>` so that setups inside async lambdas and standalone setups after an `await` work correctly.

The generated mock members are fully static and reflection-free. Two runtime helpers use delegates — `Any(mock.Method)` inspects `Delegate.Method` and `Mock.Raise` uses `DynamicInvoke` — both of which are AOT-safe; no runtime `Emit`, no `Castle.DynamicProxy`, and no `[DynamicallyAccessedMembers]` annotations are required.

## Limitations

- **Generic methods** cannot be arranged. Interface mocks throw `NotSupportedException` when an unarranged generic method is called; on class mocks, abstract generic methods are emitted as throwing stubs so the type still compiles, while non-abstract generic methods are not overridden at all and run the real base implementation.
- **`ref` / `out` / `in` parameters** are supported for recording, arrangement (`ReturnsWithOuts` / `SetsOuts`) and verification, but argument matchers cannot be used on `ref` / `out` / `in` parameters.
- **Non-virtual class members** cannot be arranged: arranging one is a compile error (`MOCK002`). This applies to `A<T>(m => m.NonVirtual(...).Returns(...))` and the standalone `m.NonVirtual(...).Returns(...)` form.
- **Unmockable types** are rejected at compile time with `MOCK001`: sealed and static classes, records, classes with no accessible constructor, and interfaces with `static abstract` members.
- **`Wrap<T>`** only supports non-generic interface types. Generic interface methods are implemented as throwing stubs (they compile but throw `NotSupportedException` when called).
