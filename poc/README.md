# Source-generation POC for Assertive

A minimal proof of concept for migrating Assertive from `System.Linq.Expressions` to a
Roslyn source generator using C# interceptors, while keeping runtime-value-rich failure
messages.

## Layout

- `generator/` — incremental generator. For every `PocAssert.That(() => ...)` call site it
  emits an `[InterceptsLocation]` method that replaces the call at compile time.
- `consumer/` — console app demonstrating the result. Generated sources are dumped to
  `consumer/generated/` for inspection (`EmitCompilerGeneratedFiles`).

Run with:

```sh
cd consumer && dotnet run
```

## What it verifies

1. **The interceptor pipeline works end-to-end** on the .NET 10 SDK: incremental generator,
   `GetInterceptableLocation` (checksum-based `InterceptsLocation` v1), the file-local
   attribute declaration, and the `<InterceptorsNamespaces>` MSBuild opt-in.
2. **Sub-expression decomposition without expression trees.** The generator parses the
   lambda body syntax, splits it on `&&` (the equivalent of `AssertionTreeProvider`), and for
   comparison leaves emits `var __left = ...; var __right = ...;` temps. Failure messages show
   live values per operand, and each operand is evaluated **exactly once** (unlike the current
   expression-tree design, which re-evaluates operands after failure). The original `Func<bool>`
   delegate is never invoked on the intercepted path.
3. **Call-site locals are reachable from the interceptor.** An interceptor swaps the method,
   not the call-site code, so it cannot name the caller's locals directly. The workaround: the
   generator knows (from the semantic model) exactly which locals the lambda captures, and the
   compiler stores captured locals as public fields *named after the variable* on the closure
   object (`condition.Target`). The interceptor reads them via reflection, casts them to their
   statically-known types, and re-evaluates sub-expressions in fully typed code. This also
   replaces `LocalsExpressionVisitor` (the "Locals:" section) with zero `<>c__DisplayClass`
   heuristics.
4. **Graceful degradation.** Call sites the generator doesn't understand still get an
   intercepted method that evaluates the delegate and reports the `[CallerArgumentExpression]`
   text. With the generator entirely absent, the runtime fallback in `PocAssert.That` works too.

## POC limitations (all addressable, just out of scope here)

- Only locals/parameters are captured; `this` members fall back (supportable: when the lambda
  captures `this`, `condition.Target` *is* the instance — or the `<>4__this` field on the
  display class — and its type is nameable, so no reflection is even needed).
- Closure field reads use uncached reflection. Real implementation: cache `FieldInfo` per call
  site, or use `UnsafeAccessor`.
- Locals captured from *different* scopes live in chained display classes; the POC assumes one.
- Only `&&` is split and only binary comparisons are decomposed; no `||`, no `Contains`/`All`
  pattern intelligence, no string diffs — in the real design those come from the existing
  runtime pattern engine, fed by a frontend-neutral node model instead of `Expression` nodes.
- Exception: **AnyPattern is ported** as a complexity gauge (`xs.Any()`, `xs.Any(x => ...)`,
  and negated forms, with filtered/unfiltered actual-count messages matching
  `Patterns/AnyPattern.cs`). Detection is semantic (verifies the call binds to
  `System.Linq.Enumerable.Any`, which the name-based runtime pattern cannot), and where the
  runtime version builds `Count()` expression trees at failure time, the generator simply
  emits a typed `Enumerable.Count(...)` call. Note the POC couples message formatting into
  the generator for expedience; the real architecture would emit a node tree and keep
  patterns as runtime classes.
- The interceptor signature still takes `Func<bool>`; the lambda/closure is still allocated at
  the call site (cheap, and needed for the closure-reading trick anyway).

## Key takeaway for the real migration

The generator's job is only to bake the *structure* (source text, operator kinds, operand
decomposition, captured-variable manifest) into per-call-site code. Value capture, pattern
matching, diffing, and formatting all stay at runtime — so Assertive's pattern layer survives,
it just needs to consume an `AssertionNode` model instead of `System.Linq.Expressions.Expression`.
