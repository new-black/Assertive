# Assertive.Mocking Migration Guide

This guide covers migrating from **Moq** and **NSubstitute** to Assertive.Mocking.

---

## 1. From Moq

| Moq | Assertive.Mocking |
|-----|-------------------|
| `new Mock<IFoo>()` | `Mock.Of<IFoo>()` |
| `mock.Object` | `mock` (the mock IS the object — no `.Object` wrapper) |
| `new Mock<IFoo>(MockBehavior.Strict)` | `Mock.Of<IFoo>(MockMode.Strict)` |
| `mock.Setup(x => x.Bar()).Returns("val")` | `A<IFoo>(x => x.Bar().Returns("val"))` |
| `mock.Setup(x => x.Bar(It.IsAny<string>())).Returns("val")` | `A<IFoo>(x => x.Bar(default).Returns("val"))` or `A<IFoo>(x => x.Bar(It.Any<string>()).Returns("val"))` |
| `mock.Setup(x => x.Void()).Callback(() => …)` | `A<IFoo>(x => x.Void().Does(() => …))` |
| `mock.Setup(x => x.Bar()).Throws<Exception>()` | `A<IFoo>(x => x.Bar().Throws(new Exception()))` |
| `mock.Verify(x => x.Bar(), Times.Once)` | `Mock.Received(mock, Times.Once, x => x.Bar())` |
| `mock.Verify(x => x.Bar(), Times.Never)` | `Mock.DidNotReceive(mock, x => x.Bar())` |
| `mock.Verify(x => x.Bar(), Times.AtLeastOnce)` | `Mock.Received(mock, Times.AtLeastOnce, x => x.Bar())` |
| `mock.VerifyNoOtherCalls()` | `Mock.VerifyNoOtherCalls(mock)` |
| `mock.Reset()` | `Mock.Reset(mock)` |

### Key differences from Moq

- **No `.Object`**: `Mock.Of<IFoo>()` returns the mock directly as `IFoo`. There is no wrapper object.
- **Arrange scope is explicit**: Setup calls happen inside an `A<T>(x => { … })` lambda, not inline on the mock variable. This prevents accidental arrangement outside setup.
- **`default` as the "any" matcher**: Instead of `It.IsAny<string>()`, you can use bare `default` inside an arrange/received lambda. Both forms work.
- **Interceptors, not Reflection.Emit**: The mock class is generated at compile time by a Roslyn source generator. No runtime proxy generation, no Castle DynamicProxy dependency — fully AOT-safe.

---

## 2. From NSubstitute

| NSubstitute | Assertive.Mocking |
|-------------|-------------------|
| `Substitute.For<IFoo>()` | `Mock.Of<IFoo>()` |
| `sub.Bar().Returns("val")` | `A<IFoo>(x => x.Bar().Returns("val"))` |
| `sub.Bar(Arg.Any<string>()).Returns("val")` | `A<IFoo>(x => x.Bar(default).Returns("val"))` |
| `sub.Received().Bar()` | `Mock.Received(sub, x => x.Bar())` |
| `sub.DidNotReceive().Bar()` | `Mock.DidNotReceive(sub, x => x.Bar())` |
| `sub.Received(2).Bar()` | `Mock.Received(sub, Times.Exactly(2), x => x.Bar())` |
| `sub.When(x => x.Bar()).Do(_ => …)` | `A<IFoo>(x => x.Bar().Does(() => …))` |
| `sub.Bar().Throws(new Exception())` | `A<IFoo>(x => x.Bar().Throws(new Exception()))` |

### Key differences from NSubstitute

- **Arrange scope is explicit and safe**: NSubstitute allows calling `.Returns(…)` directly on the substitute object, which is concise but fragile — a bare `.Returns` call outside a setup context silently does nothing. Assertive.Mocking requires the explicit `A<T>(x => { … })` scope and throws at runtime (and warns at compile time via MOCK005) if you call an arrange verb outside it.
- **No fluent proxy for received calls**: NSubstitute's `sub.Received().Bar()` style returns a checking proxy. Assertive.Mocking uses explicit static methods: `Mock.Received(sub, x => x.Bar())`.
- **Ordered verification**: `Mock.InOrder(mock, g => { g.A(); g.B(); })` verifies that `A` was called before `B` (interleaving allowed).

---

## 3. Key Conceptual Differences

### No `.Object` property

Assertive.Mocking mocks are plain objects — `Mock.Of<IFoo>()` returns an `IFoo` directly. You pass it to code under test the same way you would pass any `IFoo`. There is no `.Object` unwrapping step.

### Explicit arrange scope

Setups live inside an `A<T>(x => { … })` lambda (or `When(x => { … })` for conditional setups). This makes it unambiguous where arrangement ends and where real code begins:

```csharp
var greeter = A<IGreeter>(g =>
{
    g.Greet("Alice").Returns("Hi Alice");
    Any(g.Greet).Returns("Hello");   // fallback for any other name
});
```

Arrange verbs called outside such a scope throw `InvalidOperationException` at runtime and emit a MOCK005 compile-time warning.

### Assertive-grade failure messages

Verification failures go through Assertive's `GeneratedAssert` pipeline, so you get the same rich, context-aware failure messages as the rest of your Assertive assertions: expected-vs-actual diffs, string character-level diffs, and the full received-calls log.

### AOT safety

Mocks are emitted as plain C# classes by a Roslyn incremental source generator at build time. No `Reflection.Emit`, no Castle DynamicProxy, no runtime IL generation. Generated mocks are fully compatible with Native AOT publishing and aggressive IL trimming.
