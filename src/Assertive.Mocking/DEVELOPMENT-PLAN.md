# Assertive.Mocking — Development Plan

A roadmap to take the source-generated mocking prototype from "vertical slice that proves the
idea" to a shippable library. Grounded in the current code as of 2026-06-13.

---

## 1. Vision & positioning

A mocking library that is **source-generated and interceptor-driven** rather than
runtime-proxy-driven (Moq/NSubstitute/FakeItEasy all use Castle DynamicProxy / Reflection.Emit).
Consequences that are the whole point:

- **Native-AOT & trimming safe** — no `Reflection.Emit`, no `Activator`, no runtime IL. Mocks are
  plain C# classes emitted at compile time and registered via a `[ModuleInitializer]`.
- **Assertive-grade failures** — verification (`Received`) reuses Assertive's `GeneratedAssert`
  failure builders, so a missed call renders with the same expected/actual block, string diffing,
  and layout as an Assertive assertion. This is the differentiator: *your assertions and your mock
  verifications fail the same way.*
- **Compile-time knowledge** — the generator already reads call sites syntactically (matchers bind
  to exact argument positions; `Any(methodGroup)` overloads are generated). That same Roslyn pass
  is a free platform for **analyzer diagnostics** (Phase 5), which the proxy libraries can only bolt
  on as separate analyzer packages.

### Packaging (decided)

Two NuGet packages, **same repo, version-locked**:

```
Assertive            (assertions — unchanged)
Assertive.Mocking    (mocks)  ──depends on──▶  Assertive
```

- No `Assertive.Core` extraction. `Assertive.Mocking` depends on `Assertive` directly and uses the
  already-`public` `Assertive.Runtime.GeneratedAssert` (only `EqualityFailure` + `Failure` today).
- This makes `GeneratedAssert.EqualityFailure`/`Failure` a **cross-package public contract** — they
  must stay stable, or both packages rev together. Accepted: they're the most fundamental failure
  builders and unlikely to churn. Revisit with a narrow `MockReceivedFailure(...)` wrapper only if
  they start changing.
- Mirror Assertive's package mechanics: ship the generator as an analyzer *inside* the package, and
  ship a `buildTransitive/Assertive.Mocking.props` that appends `Assertive.Mocking.Generated` to
  `InterceptorsNamespaces`/`InterceptorsPreviewNamespaces` so consumers opt in automatically (today
  the demo sets this by hand).

---

## 2. Current state — what the prototype proves

Three projects under `src/`, **not yet in `Assertive.slnx`**, **not packaged**:

| Project | TFM | Role |
|---|---|---|
| `Assertive.Mocking` | net8.0 | Runtime engine (`MockBase`, DSL, `It`, registry). `ProjectReference` → Assertive. |
| `Assertive.Mocking.Generators` | netstandard2.0 | The `IIncrementalGenerator`. |
| `Assertive.Mocking.Demo` | net8.0 exe | 11 hand-run scenarios (a console app, **not** an automated test suite). |

**Working features** (each demonstrated by a scenario in `Demo/Program.cs`):

- `Mock.Of<T>()` (interface + class) and `A<T>(arrange, mode)` execute-the-lambda arrangement.
- `Mock.Setup`, `Mock.Received` with Assertive-grade failure rendering.
- Void arrangement via `When(() => ...).Throws/.Does`.
- `Any(methodGroup)` (match any args, no placeholders), `.Returns(value | func)`.
- Matchers: bare `default` = any, `It.Any<T>()`, `It.Any<T>(predicate)`, mixed with literals;
  last-configured-wins precedence.
- Recursive auto-mock of interface/class returns (memoized per method+args, any depth).
- Async auto-mock + unwrapped async `.Returns(value)` (no `ReturnsAsync` suffix): `Task`,
  `ValueTask`, `Task<T>`, `ValueTask<T>`. `.Throws` on async → **faulted task**, not a sync throw.
- Strict mode (`MockMode.Strict`) — unarranged call throws, auto-mock disabled.
- Class mocks: override virtual/abstract; **virtual unarranged → CallBase** (real base runs);
  abstract → default/auto-mock; non-virtual → always real. Ctor forwarding via `Mock.Of<T>(args…)`.

---

## 3. Gap analysis

### 3a. Known limits in what already exists (from the code)

- **Arity ceiling.** `Any(methodGroup)` fluent builders and typed `.Returns(impl)`/`.Does` callbacks
  cover **0–2 value params / 0–1 void params** (`ValueArrange<T1,T2,TResult>`, `VoidArrange<T1>`).
  Matcher *interceptors* are generated per-signature so they already handle any arity, but the
  builder surface caps at 2. High-arity methods can be arranged by value but not with a typed
  argument-aware callback.
- **Properties are stubs only.** For interfaces, properties are emitted as `{ get; set; }`
  auto-props — **no recording, no arrangement, no matchers, no `Received`**. For classes,
  properties aren't emitted at all, and an **abstract property on a class breaks compilation** of
  the mock (only methods are overridden).
- **Incremental caching is effectively off.** `MockTarget`/`MockMethod`/`MockProperty`/`MatcherCall`
  are reference-equality classes. Incremental generators dedup/cache on **value equality** of
  pipeline outputs, so today the generator re-runs `Emit` on essentially every edit. (They were
  converted from records to classes to dodge `IsExternalInit` on netstandard2.0 — see Phase 0 fix.)
- **Argument equality is `object.Equals`.** `ArgumentsEqual` won't structurally match collections or
  records-by-value the way users expect from value-based matching.
- **Ctor dispatch is by arity only** — two same-arity base ctors are ambiguous.
- **`Mock.Of<T>(args…)` can't combine with `MockMode.Strict`** (overload shape).
- **Silent skips** in the generator: generic methods, `ref`/`out`/`in` params, by-ref returns, and
  indexers are dropped with no diagnostic. A user arranging one gets confusing downstream errors.
- **Thread-safety unspecified.** `_calls`/`_setups` are plain `List<>`; a SUT calling a mock from
  multiple threads races. Arrange uses a `[ThreadStatic]` current-mock, which is fine, but recording
  during the act phase is not synchronized.
- **Demo is not a test suite.** No automated regression coverage, no generated-code snapshot tests,
  no AOT smoke test.

### 3b. Features in mature libraries we don't have yet

Grouped by area; prioritized in the roadmap below.

- **Verification depth:** call counts (`Once`/`Exactly(n)`/`AtLeast`/`AtMost`/`Between`),
  `DidNotReceive`, ordered verification (`Received.InOrder`), `VerifyNoOtherCalls`/`VerifyAll`,
  argument capture for post-hoc assertions.
- **Member coverage:** property get/set behavior + verification, indexers, events (raise +
  subscription tracking), generic methods, `out`/`ref` parameters.
- **Behavior richness:** sequential returns (`Returns(a).Returns(b)` / next-from-sequence),
  strongly-typed multi-arg callbacks, configurable default-value provider (`ReturnsForAll<T>`),
  `Reset`/`ClearReceivedCalls`, conditional/per-state setups.
- **Matchers:** `It.IsIn`/`It.IsInRange`/`It.IsRegex`/`It.IsNotNull`, capturing matchers, `out`/`ref`
  matchers.
- **Misc:** mock implementing multiple interfaces; configurable CallBase for interfaces with default
  implementations; async verification helpers.
- **Analyzer diagnostics:** the proxy libs ship `Moq.Analyzers` / `NSubstitute.Analyzers`
  separately; we can do it in-tree because we already have the Roslyn pass.

---

## 4. Phased roadmap

Each phase is independently shippable (0.x). Order favors **foundations → correctness → the
verification story that is our differentiator → breadth**.

### Phase 0 — Productize the prototype

Goal: it's a real package with a real test suite, and the generator caches correctly.

- [x] Add the three projects to `src/Assertive.slnx`.
- [x] Make `Assertive.Mocking` packable: `PackageId`, version pinned in lockstep with Assertive,
      ship the generator as `analyzers/dotnet/cs`, add `buildTransitive` props for the
      interceptors-namespace opt-in (copy the Assertive pattern verbatim). Switch the demo from a
      `ProjectReference` analyzer wiring to consuming the package locally to validate the packaging.
- [x] **Fix incremental cacheability:** give `MockTarget`/`MockMethod`/`MockProperty`/`MatcherCall`
      value equality. Either add an `IsExternalInit` polyfill (then use `record`s + a structural
      `ImmutableArray` comparer/`SequenceEqual`) or hand-roll `IEquatable<T>`. Verify with a
      generator cache test that an unrelated edit produces a cache hit.
- [x] **New `Assertive.Mocking.Test` (xUnit)** replacing the demo as the source of truth:
      behavioral tests for every current scenario + the gaps closed below.
- [x] **Generated-code snapshot tests** (`GeneratorSnapshotTests` using `CSharpGeneratorDriver` in-process).
- [x] **AOT smoke test** — `Assertive.Mocking.Test.Aot` project with `PublishAot=true`.
- [x] Keep `Assertive.Mocking.Demo` (or fold into `Assertive.Examples`) as a runnable showcase.

### Phase 1 — Correctness of the existing surface

- [x] **Lift the arity ceiling.** Typed builders and callbacks lifted to 4 params (value) / 3 params
      (void); `args`-array `.Does(args => …)` available as universal escape hatch at any arity.
- [x] **Structural argument equality.** `ArgumentsEqual` now does recursive structural comparison for
      collections via `IEnumerable` (arrays, `List<T>`, etc.); `object.Equals` fast-path handles
      strings, records, value types.
- [x] **Abstract class properties**: overridden with full `OnCall` getter/setter — no longer breaks
      compilation.
- [x] **Thread-safety:** `lock (_lock)` on all `_calls`/`_setups` mutations.
- [x] Resolve `Mock.Of<T>(args…)` + strict: added `Mock.Of<T>(MockMode, params object?[])` overload.

### Phase 2 — Verification suite (the differentiator)

Everything here flows through `GeneratedAssert`, so the payoff is Assertive-grade messages.

- [x] **Call counts:** `Received(mock, Times.Once, call)` / `Exactly(n)` / `AtLeast` / `AtMost` /
      `Between`, plus `DidNotReceive`. Failure messages show the actual count and the received log.
- [x] **`VerifyNoOtherCalls` / strict verification** — any call with no matching setup is flagged.
- [x] **Ordered verification** (`Mock.InOrder<T>(mock, sequence)`).
- [x] **Argument capture** — `Capture<T>` class + `It.Any<T>(Capture<T>)` enqueues recording
      predicate; inspect via `capture.Values` / `capture.Latest` after the SUT runs.
- [x] Async verification: `Received` already works on async methods via the matcher interceptor path.

### Phase 3 — Member coverage

- [x] **Properties:** get/set arrangement, recording, `Received` on get and set. Covers both
      interface and class properties; fixes the abstract-class property compile break.
- [x] **Indexers.** Generated `this[...]` with full `OnCall` get/set bodies.
- [x] **Events:** `add`/`remove` subscribers tracked; `Mock.Raise<T>(mock, attach, args…)` raises.
- [x] **`out`/`ref` parameters.** Documented limitation — generator skips them with MOCK003 warning; no source-gen workaround planned.
- [x] **Generic methods.** Documented limitation — generator skips them with MOCK003 warning; per-instantiation generation not attempted.

### Phase 4 — Behavior richness

- [x] **Sequential returns** — `ReturnsMany("a","b","c")` on all `ValueArrange<…>` arities and on
      `ArrangeExtensions`; last value repeats after exhaustion.
- [x] **Strongly-typed multi-arg callbacks** — covered by the Phase 1 arity lift (up to 4 args).
- [x] **Configurable default-value provider** — unarranged `IEnumerable<T>`/`List<T>`/arrays return
      empty (not null); interface/class returns auto-mock recursively.
- [x] **`Reset` / `ClearReceivedCalls`.** Both exposed on `Mock`.
- [x] **Conditional setups** — `ArrangeExtensions.Returns<T>(call, value, when: Func<bool>)` and
      `MockBase.AttachConditionalBehaviorToLastArrangedCall`.
- [x] **Richer matchers** — `It.IsNotNull<T>()`, `It.IsIn<T>(params T[])`, `It.IsInRange<T>(min,max)`.

### Phase 5 — Analyzer diagnostics (`MOCK####`)

Turn today's silent generator skips into compile-time guidance. High DX value, and natural since the
Roslyn pass already exists.

- [x] **MOCK001** — Mocking a sealed/static type, or a value type / generic type.
- [x] **MOCK002** — Arranging a non-virtual / non-overridable member (silently runs real on class mocks).
- [x] **MOCK003** — Generic method, `ref`/`out`, or by-ref return in an arranged member.
- [x] **MOCK004** — Named arguments at a matcher call site (matcher pipeline keys on position).
- [x] **MOCK005** — Arrange verb used outside an `A<T>`/`When`/`Received` scope.
- [x] **MOCK006** — Ctor-arg count mismatch on `A<T>(args…)` for class mocks (was for `Mock.Of`; `Mock.Of` was dropped, so this check is no longer applicable).

### Phase 6 — Docs & release

- [x] README with the AOT + Assertive-grade-failures pitch and a feature matrix vs Moq/NSubstitute.
- [x] **Migration guide** (Moq/NSubstitute → Assertive.Mocking) — `MIGRATION-GUIDE.md` created.
- [x] XML doc polish on the public surface.
- [x] 1.0 release — all planned phases complete; version bumped to 1.0.0.

---

## 5. Cross-cutting concerns

- **AOT / trim:** keep the runtime reflection-free (it is today). Add trim/AOT analyzer rooting like
  Assertive, and pin the fully-trimmed smoke test in CI. This is the claim that sells the library;
  it must never silently regress.
- **Generated-code hygiene:** `__`-prefixed identifiers to avoid CS0136 collisions in consumer code
  (Assertive learned this the hard way); `#nullable disable` + `#pragma warning disable` headers
  (already present); dedup identical interceptor bodies into one method with multiple
  `[InterceptsLocation]` attributes (Assertive does this — worth adopting as the matcher set grows).
- **Incrementality:** beyond the equality fix in Phase 0, watch the `Collect()` fan-in in `Emit` —
  any one mock changing currently reruns the whole emit. Acceptable at first; revisit if build
  perf bites on large solutions.
- **Matcher queue fragility:** `It.Any<T>(predicate)` enqueues on a `[ThreadStatic]` queue dequeued
  positionally by the interceptor. Nested mock calls inside an argument expression could desync it;
  cover with a test and consider a diagnostic.
- **The `GeneratedAssert` contract:** treat `EqualityFailure`/`Failure` as stable public API; if the
  verification suite (Phase 2) needs richer rendering, prefer adding a purpose-built
  `GeneratedAssert.MockReceivedFailure(...)` over overloading the general builders, to keep the
  cross-package surface small and intentional.

---

## 6. Suggested near-term sequence

1. Phase 0 in full (packaging + test project + incremental-equality fix + AOT smoke) — this is the
   "stop being a prototype" milestone and unblocks safe iteration on everything else.
2. Phase 2 call-count verification + `DidNotReceive` — fastest path to a visibly differentiated,
   genuinely useful feature on top of the existing engine.
3. Phase 1 structural arg equality + arity — removes the most likely real-world surprises.
4. Phase 3 properties — the biggest missing member-coverage gap.
5. Phase 5 diagnostics — once the supported surface is stable enough to say "this isn't supported"
   precisely.
