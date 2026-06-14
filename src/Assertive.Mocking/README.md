# Assertive.Mocking

Source-generated, AOT-safe mocking for .NET with Assertive-grade failure messages. Unlike Moq and NSubstitute, no runtime proxy generation or `System.Reflection.Emit` — mock types are emitted at compile time by a Roslyn source generator, making them fully compatible with Native AOT and trimming. Failure messages use the same expression-rendering engine as [Assertive](https://github.com/new-black/Assertive), so you see exactly what was called and what was expected.

## Getting started

```
dotnet add package Assertive.Mocking
```

The package ships a `buildTransitive` props file that automatically enables the `InterceptorsPreviewNamespaces` compiler feature — no manual project configuration needed.

One `using` directive is all you need:

```csharp
using static Assertive.Mocking.Mock;
```

## Core API

### Creating mocks

```csharp
// Simple creation
var repo = A<IUserRepository>();

// Create and arrange in one step
var repo = A<IUserRepository>(m =>
{
    m.GetById(1).Returns(new User { Id = 1, Name = "Alice" });
});

// Strict mode — unarranged calls throw instead of returning defaults
var repo = A<IUserRepository>(MockMode.Strict);
var repo = A<IUserRepository>(m => { m.GetById(1).Returns(new User()); }, MockMode.Strict);
```

### Arrangement verbs: `Returns`, `Throws`, `Does`

```csharp
var repo = A<IUserRepository>();

repo.GetById(42).Returns(new User { Id = 42 });

repo.Delete(99).Throws(new NotFoundException("user 99 not found"));

repo.Save(default!).Does(args =>
{
    var user = (User)args[0]!;
    Console.WriteLine($"Saving {user.Name}");
    return user;
});
```

### Void methods: `When(...).Throws` / `When(...).Does`

```csharp
var bus = A<IEventBus>();

When(() => bus.Publish(default!)).Throws(new InvalidOperationException("bus offline"));

When(() => bus.Publish(default!)).Does(args =>
{
    log.Add((IEvent)args[0]!);
});
```

### Method-group arrangement with `Any`

`Any` arranges all calls to a method at once, without a specific argument value at the call site. The typed overloads give you the argument values in the callback.

```csharp
var repo = A<IUserRepository>();

// Arrange all calls to GetById regardless of argument
Any(repo.GetById).Returns(new User { Id = 0, Name = "fallback" });

// Computed return — typed argument passed to the function
Any(repo.GetById).Returns((int id) => new User { Id = id });

// Void method via Any
Any(bus.Publish).Does((IEvent e) => log.Add(e));

// Sequential returns via Any
Any(repo.GetById).ReturnsMany(new User { Id = 1 }, new User { Id = 2 });
```

### Matchers

When you pass `default` as an argument, or use any `It.*` matcher, the call matches any invocation where that argument satisfies the condition.

```csharp
// default — match any value for that argument position
repo.GetById(default).Returns(new User());

// It.Any<T>() — same as default, but explicit
repo.GetById(It.Any<int>()).Returns(new User());

// It.Any<T>(predicate) — conditional match
repo.GetById(It.Any<int>(id => id > 0)).Returns(new User());

// It.IsNotNull<T>()
repo.Save(It.IsNotNull<User>()).Returns(true);

// It.IsIn(...)
repo.GetById(It.IsIn(1, 2, 3)).Returns(new User());

// It.IsInRange(min, max)
repo.GetById(It.IsInRange(1, 100)).Returns(new User());
```

### Capturing arguments

```csharp
var capture = new Capture<User>();

repo.Save(It.Any<User>(capture)).Returns(true);

sut.CreateUser("Alice");

Assert(() => capture.Latest.Name == "Alice");
// capture.Values — IReadOnlyList<User> of every captured value
```

### Properties

Property getters are arranged and verified just like methods. Property setters are recorded.

```csharp
var svc = A<INamedService>(s =>
{
    s.Name.Returns("Alice");
});

Assert(() => svc.Name == "Alice");

// Verifying a property was read
Mock.Received(svc, s => _ = s.Name);

// Verifying a property was set to a specific value
svc.Name = "Bob";
Mock.Received(svc, s => s.Name = "Bob");
```

### Indexers

```csharp
var cache = A<ICache>(c =>
{
    c["key"].Returns("value");
});

var result = cache["key"]; // "value"

// Verifying indexer set
cache["key"] = "newValue";
Mock.Received(cache, c => { c["key"] = "newValue"; });
```

### Events

Use `Mock.Raise` to fire an event on a mock and exercise subscribers.

```csharp
var source = A<IEventSource>();
string? received = null;
source.MessageReceived += (sender, msg) => received = msg;

Mock.Raise(source, s => s.MessageReceived += null, null, "hello");

Assert(() => received == "hello");
```

### Verification

```csharp
// Lambda form — mock is inferred from the call
Received(() => repo.GetById(42));
DidNotReceive(() => repo.Delete(default));

// Explicit mock form — useful when the lambda form is ambiguous
Mock.Received(repo, m => m.GetById(42));
Mock.DidNotReceive(repo, m => m.Delete(default));

// Call-count verification
Mock.Received(repo, Times.Exactly(2), m => m.GetById(42));
Mock.Received(repo, Times.AtLeastOnce, m => m.Save(default!));
Mock.Received(repo, Times.Never, m => m.Delete(default));

// Assert no calls were made beyond those with matching setups
Mock.VerifyNoOtherCalls(repo);
```

Available `Times` values: `Times.Once`, `Times.Never`, `Times.AtLeastOnce`, `Times.AtMostOnce`, `Times.Exactly(n)`, `Times.AtLeast(n)`, `Times.AtMost(n)`, `Times.Between(min, max)`.

### Call-order verification

```csharp
Mock.InOrder(repo, m =>
{
    m.BeginTransaction();
    m.Save(default!);
    m.Commit();
});
```

The sequence is a subsequence check — other calls may be interleaved between the listed ones, but the listed calls must appear in the given order.

### Sequential returns

Values are returned in order; the last value is repeated once the sequence is exhausted.

```csharp
repo.GetById(default).ReturnsMany(
    new User { Id = 1 },
    new User { Id = 2 },
    new User { Id = 3 }
);
```

### Conditional setup

A setup can be made conditional — it only activates when the condition holds at call time. When the condition is false the setup is skipped and the next matching setup (or default) is used.

```csharp
bool paused = false;
repo.GetById(default).Returns(new User(), when: () => !paused);
```

### Strict mocking

In strict mode, any call that has not been explicitly arranged throws a `StrictMockException` with a detailed message listing what was called and what setups exist.

```csharp
var repo = A<IUserRepository>(MockMode.Strict);
var repo = A<IUserRepository>(m =>
{
    m.GetById(1).Returns(new User());
}, MockMode.Strict);
```

### Auto-mock

In loose mode (the default), calls that return an interface or class and have no arrangement automatically return a stable child mock — the same mock is returned on repeated calls with the same arguments. `Task<T>` and `ValueTask<T>` are unwrapped so the returned mock is the `T`, not a task wrapping it.

```csharp
var factory = A<IServiceFactory>();

// No arrangement needed — returns a consistent child mock
var repo = factory.GetRepository("users");
var sameRepo = factory.GetRepository("users"); // identical instance
```

### Class mocking

Concrete classes with virtual or abstract members can be mocked. Non-virtual members always run the real implementation. Virtual members that have not been arranged call the base implementation.

```csharp
// Default constructor
var svc = A<MyService>();

// Forwarding constructor arguments
var svc = A<MyService>("connection-string", 30);

// Arrange a virtual method
svc.ComputeHash(default!).Returns("fake-hash");
```

### Building the system under test with `Build<T>`

`Build<T>` constructs a class using its richest accessible constructor. Each provided argument is matched to a constructor parameter by type (not by position); unmatched parameters are auto-mocked. The source generator emits a typed factory per call site — no reflection at runtime.

```csharp
// All constructor params auto-mocked
var svc = Build<CheckoutService>();

// Provide specific deps; the rest are auto-mocked
var gateway = A<IPaymentGateway>(g => g.Charge(It.Any<decimal>()).Returns("ok"));
var svc = Build<CheckoutService>(gateway);

// Arguments matched by type, not position — order doesn't matter
var svc = Build<CheckoutService>(logger, gateway);
```

### Spying on real objects with `Wrap<T>`

`Wrap<T>` wraps an existing interface implementation: all calls delegate to the real object by default, but every call is recorded, and specific calls can be overridden with `Setup`.

```csharp
var spy = Wrap<ITransformer>(new UpperCaseTransformer());

// Calls through to the real implementation
Assert(() => spy.Transform("hello") == "HELLO");

// Recorded — can be verified
Received(() => spy.Transform("hello"));

// Override a specific call
Setup(spy, s => s.Transform("hello").Returns("OVERRIDDEN"));
Assert(() => spy.Transform("hello") == "OVERRIDDEN");
Assert(() => spy.Transform("world") == "WORLD"); // not overridden → calls through
```

### Post-creation setup with `Setup<T>`

When a mock was created outside an `A<T>(arrange)` lambda — for example after `Wrap<T>` — use `Arrange<T>` to add arrangements later:

```csharp
var spy = Wrap<ITransformer>(new UpperCaseTransformer());

Setup(spy, s =>
{
    s.Transform("special").Returns("SPECIAL");
    When(() => s.Process("bad")).Throws(new InvalidOperationException());
});
```

### Resetting mock state

```csharp
// Clear the recorded calls log (keep setups)
Mock.ClearReceivedCalls(repo);

// Clear both calls and setups
Mock.Reset(repo);
```

---

## Feature matrix

| Feature | Assertive.Mocking | Moq | NSubstitute |
|---|:---:|:---:|:---:|
| AOT / trimming safe | ✓ | ✗ | ✗ |
| Compile-time mock generation | ✓ | ✗ | ✗ |
| Assertive-grade failure messages | ✓ | ✗ | ✗ |
| Fluent arrange syntax | ✓ | ✓ | ✓ |
| Argument matchers | ✓ | ✓ | ✓ |
| Argument capture | ✓ | ✓ | ✓ |
| Call-count verification | ✓ | ✓ | ✓ |
| Call-order verification | ✓ | ✗ | ✗ |
| Strict mode | ✓ | ✓ | ✓ |
| Class mocking | ✓ | ✓ | ✓ |
| CallBase (unarranged virtuals) | ✓ | ✓ | ✓ |
| Auto-mock (unarranged returns) | ✓ | ✗ | ✓ |
| Sequential returns | ✓ | ✓ | ✓ |
| Property get/set | ✓ | ✓ | ✓ |
| Indexers | ✓ | ✓ | ✓ |
| Event raise/verify | ✓ | ✓ | ✓ |
| Spy wrapper (`Wrap<T>`) | ✓ | ✗ | ✓ |
| SUT auto-construction (`Build<T>`) | ✓ | ✗ | ✗ |
| Generic methods | ✗ | ✓ | ✓ |
| ref / out parameters | ✗ | ✓ | ✓ |

---

## Known limitations

- **Generic methods** are silently skipped by the source generator. Calls to generic methods on a mock will invoke the real implementation (or throw on interfaces).
- **`ref` / `out` / `in` parameters** are not supported. Methods with by-ref parameters are skipped.
- **Sealed classes** cannot be mocked — the generator requires a type it can subclass.
- **Non-virtual class members** always run the real implementation regardless of any arrangement.
- **`Wrap<T>`** only supports non-generic interface types.

---

## How it works

When you install the package, a Roslyn incremental source generator scans your compilation for calls to `A<T>()`, `Wrap<T>()`, `Build<T>()`, and the arrangement verbs (`Returns`, `Throws`, etc.). For each mocked type it emits a concrete subclass (for classes) or interface implementation (for interfaces) that overrides every eligible member. Each generated member records the call in the mock's internal log, checks for matching setups, and either returns the configured value, invokes the base, or returns a sensible default.

`Wrap<T>` generates a similar class but holds a reference to the wrapped instance and calls through to it when no arrangement matches.

`Build<T>` is intercepted per call site: the generator reads the declared types of each argument from Roslyn's semantic model, matches them to constructor parameters by type, and emits a typed factory that auto-mocks any unmatched parameters — no reflection at runtime.

Argument matchers (`It.Any<T>()`, `default`, etc.) are recognised syntactically at the call site. The generator emits a companion interceptor for each matching call that registers the matcher predicates before the actual call is recorded, so the runtime never has to guess which positions were matchers.

The result is a fully static, reflection-free mock implementation that the AOT compiler can see in its entirety — no `Emit`, no `Castle.DynamicProxy`, no `[DynamicallyAccessedMembers]` annotations required.
