# Assertive vNext: Source-Generator Architecture

**Status:** Draft for review
**Goal:** Replace the `System.Linq.Expressions` frontend with a Roslyn source generator
(C# interceptors) while reaching message parity with the current implementation.
**Companion:** [`poc/`](../poc/README.md) — working proof of concept (interceptor pipeline,
closure-based value capture, `&&` splitting, comparison decomposition, AnyPattern port).

---

## 1. Motivation

The current design analyzes `Expression<Func<bool>>` trees at runtime. It works well, but:

- **Expression trees are frozen.** The C# team no longer evolves them. `?.`, pattern
  matching, `switch` expressions, tuples literals, ranges in some forms, and every future
  language feature are inexpressible in an assertion. The gap widens every C# release.
- **Every assertion pays runtime costs even on success**: tree allocation +
  `Lambda.Compile(preferInterpreter: true)` + interpreted execution per call.
- **Failure analysis re-evaluates sub-expressions** (`ExpressionHelper.EvaluateExpression`,
  `NullReferenceVisitor`), which re-runs side effects and can mis-attribute causes for
  stateful or non-deterministic expressions.
- The runtime carries heuristics to undo compiler lowering: closure-class detection
  (`LocalsExpressionVisitor`), enum `Convert` stripping (`ExpressionRewriter`), extension
  method instance-syntax recovery (`GetInstanceOfMethodCall`), ref-struct interpreter
  workarounds (`ShouldUseInterpreter`).

A source generator sees the assertion *syntax* at compile time and emits per-call-site code
that evaluates each sub-expression exactly once, captures values into a runtime model, and
hands that model to the existing pattern/formatting engine. Analysis intelligence stays at
runtime; only the *structure* moves to compile time.

## 2. Goals and non-goals

### Goals

1. **Message parity**: for every assertion expressible today, the failure message is
   equivalent to (or strictly better than) the current output — verified mechanically
   (§13).
2. All 18 friendly-message patterns, all 10 exception patterns, locals display, custom
   pattern DSL, configuration (quotation patterns, colors, output limits), `Assert.Throws`
   (incl. async + exception predicates), `Assert.Snapshot`, all five test-framework
   adapters.
3. Single-evaluation semantics: no sub-expression is evaluated more than once per
   assertion.
4. Zero-configuration packaging: `dotnet add package Assertive` is all a consumer does.
5. Graceful degradation when the generator cannot decompose a call site (§8.8).

### Non-goals (for the parity milestone)

- Supporting expression shapes the generator can't decompose *better* than the fallback
  tier (e.g. method-group predicates, block-bodied lambdas).
- AOT/trimming certification (the design improves the story, but it is not a parity
  criterion).
- New patterns exploiting new C# syntax (`is` patterns, `?.`, `switch` expressions). The
  *evaluator* must handle them correctly (they now compile!); pattern intelligence for
  them is follow-up work.
- Keeping the `Expression<Func<bool>>` public API working forever. It survives internally
  as a test oracle (§13) and may ship one transition release, but vNext is a major version
  with a breaking API change (§6).

## 3. Constraints established by the POC

These shape everything below; see `poc/` for working code.

1. **Interceptors substitute the method, not the call site.** The generated method receives
   the original arguments. Therefore the parameter must become `Func<bool>` — if it stayed
   `Expression<Func<bool>>`, the call site would still build the tree and still reject
   non-tree syntax.
2. **`Func<bool>` and `Expression<Func<bool>>` overloads are ambiguous** (CS0121, verified
   empirically). The two APIs cannot coexist under one method name. This forces a major
   version break (§6).
3. **Interceptor code cannot name call-site locals**, but the closure object can be read:
   captured locals are public fields named after the variable on `delegate.Target`'s
   display class; a `this` capture surfaces the (nameable) declaring type. The generator
   knows statically which names to read and their types (POC-verified).
4. **Checksum-based `[InterceptsLocation]` (v1)** via `SemanticModel.GetInterceptableLocation`
   works on the .NET 10 SDK with `<InterceptorsNamespaces>` opt-in.

## 4. Architecture overview

```
┌────────────────────────────────────────────────────────────────────┐
│ Consumer test project                                              │
│                                                                    │
│  Assert.That(() => user.Address.Street.Length == 10)               │
│        │ intercepted at compile time                               │
│        ▼                                                           │
│  [generated] Assert_42(Func<bool> a, string expr, ...)             │
│    - reads captured vars from closure / this                       │
│    - evaluates temps in C# order, step-tracked, try/catch          │
│    - on failure: builds EvaluatedNode graph (values plugged into   │
│      a static structure table), calls runtime engine               │
└────────────────────────┬───────────────────────────────────────────┘
                         ▼
┌────────────────────────────────────────────────────────────────────┐
│ Assertive runtime (existing code, re-based)                        │
│                                                                    │
│  EvaluatedNode graph ──► FriendlyMessageProvider (patterns)        │
│                     ──► ExceptionPatterns (NRE, IOOR, ...)         │
│                     ──► FriendlyMessageFormatter (sections,        │
│                          colors, quotation, locals, serializer)    │
│                     ──► CustomPatternRegistry (DSL templates)      │
│                     ──► Test-framework adapters, Snapshots         │
└────────────────────────────────────────────────────────────────────┘
```

Three deliverables:

| Component | Contents |
|---|---|
| **Runtime** (`Assertive.dll`, net8.0) | `EvaluatedNode` model; patterns/formatters/serializer re-based onto it; degraded-mode entry points; snapshot + Throws runtime; framework adapters. |
| **Generator** (`Assertive.Generators.dll`, netstandard2.0 analyzer) | Incremental generator emitting interceptors; ships *inside* the main package under `analyzers/`. |
| **Build assets** | `buildTransitive/Assertive.props` injecting `<InterceptorsNamespaces>` (+ legacy `<InterceptorsPreviewNamespaces>`). |

The principle that keeps complexity bounded: **the generator emits structure and captures
values; it contains no message intelligence.** Every pattern — built-in, exception, and
user-registered — remains a runtime class. The generator's output is a uniform
`EvaluatedNode` graph, regardless of which patterns later match it.

## 5. Public API (vNext)

```csharp
public static class Assert
{
  public static void That(
    Func<bool> assertion,
    object? message = null,
    Func<object?>? context = null,
    [CallerArgumentExpression(nameof(assertion))] string assertionExpression = "",
    [CallerArgumentExpression(nameof(context))] string? contextExpression = null);

  // Throws: action parameter unchanged (already CallerArgumentExpression-based).
  // The exception predicate becomes a delegate; the generator lifts its body.
  public static void Throws(
    Action action,
    Func<Exception, bool>? exceptionAssertion = null,
    [CallerArgumentExpression(nameof(action))] string actionExpression = "",
    [CallerArgumentExpression(nameof(exceptionAssertion))] string? exceptionExpression = null);

  // + Throws<TException>, Func<object?>, Func<Task> variants — same transformation.
  // Assert.Snapshot: unchanged (already expression-tree-free).
}
```

Notes:

- `DSL.Assert(...)` mirrors stay; the generator recognizes both entry points by symbol.
- The non-intercepted bodies are fully functional degraded implementations (§8.8), so the
  library still *works* (with reduced messages) wherever the generator doesn't run.
- Binary breaking, source-compatible for lambda-literal call sites. Call sites that passed
  a stored `Expression<Func<bool>>` variable break and have no equivalent (§14).

## 6. The `EvaluatedNode` model

The frontend-neutral replacement for `System.Linq.Expressions.Expression` as the *input to
patterns*. (Note: the existing `Analyzers.AssertionNode` — the And/Or/Leaf boolean tree —
keeps its role but its leaves become `EvaluatedNode`s instead of `Expression`s.)

```csharp
public enum EvaluatedNodeKind
{
  // mirrors the subset of ExpressionType the patterns actually consume:
  Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
  Call, Member, Index, Constant, Identifier, Not, Convert, TypeIs, Conditional,
  ConditionalAccess, Lambda, Other
}

public sealed class EvaluatedNode
{
  public EvaluatedNodeKind Kind { get; }
  public string SourceText { get; }            // exact, as written
  public Type? StaticType { get; }
  public string? MemberName { get; }           // "Any", "Length", "Equals", ...
  public string? ContainingTypeFullName { get; } // "System.Linq.Enumerable", ...
  public bool IsExtensionMethodSyntax { get; } // instance-style call?
  public EvaluatedNode? Receiver { get; }      // instance / left operand
  public IReadOnlyList<EvaluatedNode> Arguments { get; }

  // Value capture — populated by the evaluator on the failure path:
  public bool WasEvaluated { get; }            // false if short-circuited / after throw
  public object? Value { get; }
  public Exception? ThrownException { get; }   // set on the node whose evaluation threw
}
```

Key differences from `Expression` that the pattern port must absorb:

| Today (Expression) | vNext (EvaluatedNode) |
|---|---|
| `expr.NodeType == ExpressionType.Equal` | `node.Kind == EvaluatedNodeKind.Equal` |
| `Method.Name == "Any"` (name string only) | `MemberName == "Any" && ContainingTypeFullName == "System.Linq.Enumerable"` — strictly more precise |
| `ExpressionHelper.EvaluateExpression(sub)` (re-evaluates; side effects) | `sub.Value` (captured once, during the only evaluation) |
| `ExpressionStringBuilder.ExpressionToString(expr)` (reconstruction) | `node.SourceText` (exact) |
| `GetInstanceOfMethodCall` heuristic | `Receiver` + `IsExtensionMethodSyntax`, known at compile time |

`FailedAssertion` becomes `{ EvaluatedNode Node, Exception? Exception, bool IsNegated, ... }`;
`IFriendlyMessagePattern` and `IExceptionHandlerPattern` keep their shape. The port of the
18 + 10 patterns is mostly mechanical (the POC's AnyPattern port measured ~1:1 in size).

**Value-capture policy:** every temp the evaluator produces (operands, call receivers, call
arguments, member-chain links) maps to a node and is captured. This single policy serves
four consumers at once: operand display (`EqualsPattern`), custom DSL templates
(`{instance.value}`, `{arg0.value}`), exception attribution (§8.6), and locals display.
Nothing evaluates twice; nothing is evaluated *extra* (these values are computed during
normal evaluation anyway — the temps just keep them reachable).

## 7. Expression-tree adapter (transition + oracle)

A small adapter converts an `Expression` tree into an `EvaluatedNode` graph (evaluating
via the current `Compile`/`DynamicInvoke` machinery to populate values). Purpose:

1. **Phase 1 refactor safety**: re-base all patterns onto `EvaluatedNode` while the public
   API is still expression-based — the entire existing test suite validates the port
   before any generator exists.
2. **Differential testing oracle** (§13).
3. Optional transition release: vNext-1 ships the re-based engine with the old API,
   zero behavior change.

## 8. Generator design

### 8.1 Pipeline and incrementality

- `IIncrementalGenerator`; `CreateSyntaxProvider` predicate: invocation whose name token is
  `That` / `Throws` / DSL equivalents (cheap syntax check), transform: symbol check against
  `Assertive.Assert` / `Assertive.DSL`, then full call-site analysis.
- The transform returns **value-equatable models** (records of strings/arrays; no syntax
  nodes, no symbols retained) so unchanged files don't re-emit.
- Output is grouped **per source file** (one generated file per consumer file containing
  assertions), not one global `Collect()` — editing one test file regenerates one file.
  (The POC's single-file `Collect()` is a known shortcut.)
- Interception via `GetInterceptableLocation` (v1 checksum data); the
  `InterceptsLocationAttribute` declaration is emitted `file`-local.
- Generated methods get `[StackTraceHidden]` (and `[DebuggerStepThrough]` behind a config
  switch) so assertion frames don't pollute failure stack traces.

### 8.2 Call-site analysis

For each intercepted `Assert.That` whose first argument is a lambda literal:

1. **Leaf split** of the body on `&&`, `||`, `&`, `|` into the same And/Or/Leaf tree
   `AssertionTreeProvider` builds today. Execution semantics replicated exactly
   (`AssertionTreeExecutor` reference behavior): `&&` stops at the first failing leaf;
   `&`/`|` evaluate both sides and may report multiple failed leaves; a false `||` reports
   both branches.
2. **Pattern-relevant recognition on `IOperation`**, not raw syntax.
   `SemanticModel.GetOperation` provides the normalized tree (call spelling collapsed,
   parens gone, conversions explicit, `?.` structured) — the compile-time analogue of the
   expression tree. Syntax is consulted only for exact source text and for the code
   fragments pasted into the evaluator.
3. **Shared core-expression extractor**: a single `Unwrap` handles parentheses,
   null-forgiving `!`, `checked()`, and casts so every recognizer sees the core node.
   This is written once; per-pattern recognizers must not do their own unwrapping.
4. **Captured-variable manifest**: every identifier in the body classified via the
   semantic model — local/parameter (closure field), `this` member (typed `Target` cast;
   private members via emitted `[UnsafeAccessor]` shims), static (resolves directly,
   emitted fully qualified), nested-lambda parameter (locally bound in pasted code).
   Anything else ⇒ degrade to Tier 1 (§8.8).

### 8.3 Closure access

- Captured locals: read from `delegate.Target` display-class fields by name, cast to the
  semantically-known type. `FieldInfo` lookups are cached per call site in generated
  static fields; access happens **only on the failure path** (the success path never
  touches the closure).
- Because the closure *allocation strategy* (display-class chaining across scopes) is a
  compiler implementation detail, field lookup goes through one runtime helper:
  `ClosureReader.Read<T>(object target, string name)` — direct field, then bounded
  recursive search through display-class reference fields, memoized per (type, name).
  This is the single point of fragility-by-design, isolated and testable against compiler
  versions (§15, R1).
- `this` capture: `Target` is the declaring type (or holds it via a display-class field);
  the generated code casts and proceeds statically.
- Locals display: the manifest replaces `LocalsExpressionVisitor` entirely; values are
  read on failure and serialized by the existing `Serializer`.

### 8.4 Evaluation lowering

Per leaf, the generator emits straight-line code observing C# evaluation order:

```csharp
// () => user.Address.Street.Length == expectedLength && names.Any(n => n.Contains(x))
__step = 0;
try
{
  var __t0 = user;                __step = 1;
  var __t1 = __t0.Address;        __step = 2;
  var __t2 = __t1.Street;         __step = 3;
  var __t3 = __t2.Length;         // left operand value
  var __t4 = expectedLength;      // right operand value
  if (!(__t3 == __t4)) { /* build node graph, plug __t*, throw via engine */ }
}
catch (AssertiveException) { throw; }
catch (Exception __ex) { /* node graph + __step → engine exception patterns */ }
```

Rules:

- **Order**: temps in source order (left-to-right, receiver before arguments) — identical
  observable behavior to direct evaluation, including side effects, exactly once.
- **Short-circuit**: `&&`/`||` lower to nested `if`s over leaf results; `&`/`|` evaluate
  all leaves and aggregate failures.
- **Conditional access** (`a?.B()`): lowered with an explicit null check and a
  "not evaluated" marker for the skipped portion (`WasEvaluated = false`).
- **Step tracking** (`__step`) attributes any thrown exception to an exact sub-expression;
  the static step table maps index → node. This replaces the post-hoc re-evaluating
  visitors (`NullReferenceVisitor` et al.) with recorded fact: NRE at step *k* ⇒ receiver
  temp *k−1* was null (if it wasn't, the exception was thrown *inside* the member —
  today's `ExceptionWasThrownInternally` distinction, for free).
- Temps that must be readable in the catch block are hoisted typed declarations.
  Unnameable types mid-chain (anonymous types) degrade that leaf to coarse attribution.
- Fine-grained steps are emitted only for leaves containing member chains, indexers, or
  calls; `x == 5` gets no try/catch beyond the leaf-level one.

### 8.5 Node-graph emission

Structure is static; values are dynamic. Per call site the generator emits one static
`NodeDescriptor[]` table (kinds, source texts, member names, containing types, child
indices — built once, cached) and, on the failure path only, a small
`EvaluatedNode.Materialize(descriptorTable, values, thrownStep)` call that plugs the temp
values in. The success path allocates nothing beyond what the user's expression allocates.

### 8.6 Collection patterns (`All` / `Any` per-item analysis)

For `xs.All(x => body)` / negated `Any` with a lambda literal, the generator lifts the
predicate body and emits a per-item loop that reuses the same leaf-lowering recursively:

```csharp
var __idx = 0;
foreach (var x in __collection)
{
  // instrumented evaluation of `body` (same lowering as a top-level leaf),
  // collecting (index, item, per-item EvaluatedNode graph) for failures
  __idx++;
}
```

This replaces the `NamedConstantExpression` + nested-`AssertionFailureAnalyzer` machinery.
The per-item graphs flow into the same runtime `AllPattern`/`AnyPattern` logic (failing
items list, per-item cause, item serialization, `On item [i]` context for exceptions
thrown inside the predicate). Method-group predicates (`xs.All(IsValid)`) get
collection-level messages only — parity with today, which also cannot see inside them.

### 8.7 `Throws` predicate lifting

`Assert.Throws(action, ex => ex.Message.Contains("x"))`: the runtime catches the exception
as today; the generator lifts the predicate body with `ex` bound to the caught exception
and applies the standard leaf lowering — replacing today's parameter-substitution
re-analysis (`NamedConstantExpression` + recursive `That`). Async variants identical
modulo `await`.

### 8.8 Degradation tiers

| Tier | When | Behavior |
|---|---|---|
| **0 — full** | Lambda literal, all identifiers classifiable, decomposable shape | Complete decomposition, patterns, locals, exception attribution |
| **1 — intercepted, coarse** | Unsupported shape (block body, method group at top level, file-local/private-nested types in operands, unclassifiable identifier) | Generated interceptor evaluates the delegate; on failure: source text, locals that *are* readable, exception type + stack, no decomposition. Emits a build-time info diagnostic (`ASRT001`) naming the limitation. |
| **2 — not intercepted** | Generator absent (F#, csx, non-SDK builds, old toolchains) | The real method body runs: `[CallerArgumentExpression]` text + exception passthrough. |

Tier boundaries are *per leaf* where possible (one weird conjunct shouldn't degrade its
siblings).

## 9. Runtime engine changes

| Area | Change |
|---|---|
| 18 message patterns | Mechanical port to `EvaluatedNode` (POC-measured ~1:1). Name-based matches upgraded to symbol-based. |
| 10 exception patterns | Port + simplify: throwing node is supplied, all re-evaluating visitors (`LambdaAwareExpressionVisitor`, `NullReferenceVisitor`) deleted. Message wording preserved verbatim. |
| `FriendlyMessageFormatter` | Unchanged except input type; quotation patterns, colors, sections (`Expected`/`Actual`/`Locals`/cause/exception) as-is. |
| `LocalsProvider` / `LocalsExpressionVisitor` | Replaced by generator manifest + `ClosureReader`. |
| `ExpressionStringBuilder`, `ExpressionRewriter`, `ShouldUseInterpreter`, `NamedConstantExpression` | Deleted (expression-frontend adapter keeps private copies while it lives). |
| Custom pattern DSL (`PatternDefinition`, `TemplateEvaluator`) | Matching fields unchanged (already declarative). `TemplateEvaluator` reads `node.Value` instead of `EvaluateExpression`. Runtime registration fully preserved. |
| `Configuration` | Unchanged surface. `ExpressionQuotationPattern` now decorates exact source text. |
| Snapshots, framework adapters | Untouched. |

## 10. Feature parity matrix

| Feature | Mechanism in vNext | Risk |
|---|---|---|
| Boolean/comparison/equality/null/length patterns | Node port | Low |
| String diffs (`EqualsPattern`, `ContainsPattern` hints) | Runtime, values from nodes | Low |
| `Contains`/`StartsWith`/`SequenceEqual`/`Is`/`HasValue`/ReferenceEquals | Node port | Low |
| `All`/`Any`/`NotAll` incl. per-item analysis | §8.6 lifted loops | **Medium** (most complex emit) |
| Locals section incl. tuple deconstruction names | Manifest + `ClosureReader` | Medium (closure shapes, R1) |
| NRE "which component was null" | Step tracking | Low (strictly better) |
| Other 9 exception patterns | Step tracking + port | Low |
| `Throws` + predicate decomposition | §8.7 | Low |
| Custom pattern DSL | §9 | Low |
| Quotation/colors/output config | Unchanged | Low |
| Snapshots | Unchanged | None |
| `&`,`|`,`&&`,`||` multi-failure semantics | §8.4, mirrors `AssertionTreeExecutor` | Low |
| Negation handling (`!`) | Leaf-level flag (as today) | Low |
| Framework adapters (xUnit v2/v3, NUnit, MSTest, TUnit) | Unchanged | None |

## 11. Packaging and toolchain

- **One package.** `Assertive` carries lib + `analyzers/dotnet/cs` + `buildTransitive`
  props. Adapter packages unchanged.
- **Consumer floor:** SDK with checksum-interceptor support. Recommendation: document
  .NET 9 SDK as minimum, CI-verify on 9 and 10. (Runtime TFM stays net8.0;
  `UnsafeAccessor` requires net8.0+ which we already have.) *Verify exact Roslyn floor in
  Phase 0 and pin `Microsoft.CodeAnalysis` accordingly; if needed, multi-target the
  analyzer via `analyzers/dotnet/roslyn{X.Y}/cs` folders.*
- **Generator hygiene:** no banned APIs (`EnforceExtendedAnalyzerRules`), deterministic
  output, `RSEXPERIMENTAL002` suppressed knowingly for `GetInterceptableLocation`.
- Diagnostics: `ASRT001` (tier-1 degradation, info), `ASRT002` (interception impossible at
  a call site that looks like Assertive, warning), both suppressible.

## 12. Performance

| Path | Today | vNext |
|---|---|---|
| Successful assert | Tree alloc + `Compile(interpreter)` + interpreted eval (~µs) | Direct evaluation + closure alloc (~ns); no Assertive allocations |
| Failing assert | Re-compile + re-evaluate per leaf + reflection walking | Node materialization + closure reads (cached `FieldInfo`) |
| Compile time | — | One interceptor per call site; mitigations: per-file output, equatable models, measured budget in CI (large synthetic test project) |
| Binary size | — | Generated code ≈ 2× expression source per call site (POC observation); acceptable for test assemblies, tracked in CI |

## 13. Parity verification strategy

This is the heart of de-risking the migration.

1. **Phase 1 gate:** re-based engine + expression adapter passes the *entire existing test
   suite* unchanged (same public API, same messages).
2. **Differential harness:** a corpus runner takes assertion expressions compiled both
   ways — (a) expression adapter, (b) generated — and asserts message equality. Known
   intentional improvements (exact source text vs reconstruction; values-at-evaluation vs
   re-evaluation) are encoded as explicit, reviewed normalizations, not wildcards.
3. **Corpus:** every expression in the current `Assertive.Test` suite, plus a
   syntax-shape matrix (parens, casts, `!`, `checked`, conditional access, `using static`
   calls, static vs instance call spelling, multi-line lambdas, comments inside
   expressions, tuples, generic helpers) — this is where syntax-wrapper bugs die.
4. **Generator snapshot tests:** generated source verified via `Verify`-style snapshots;
   compile-and-run tests via `Microsoft.CodeAnalysis.Testing`.
5. **Closure-shape tests:** locals from same/nested/multiple scopes, `this` + locals
   mixed, structs, tuple deconstruction, loop variables — pinned against `ClosureReader`.
6. CI matrix: .NET 9/10 SDKs × all five framework adapter test projects.

## 14. Known regressions (accepted, documented)

1. **Programmatic/stored assertion expressions** (`Expression<Func<bool>>` built or passed
   as a variable): no longer expressible. No workaround; release notes + migration guide.
2. **User wrapper methods** forwarding to `Assert.That` lose decomposition (interception
   happens inside the wrapper). Mitigation: document the pattern of making wrappers take
   the values (not the assertion), or accept Tier-1 messages there.
3. **F# / scripting / generator-less builds** drop to Tier 2.
4. **`file`-local and private nested types in operands** degrade to Tier 1 (per leaf).
5. Stored-delegate arguments (`Func<bool> f = ...; Assert.That(f)`) — compile fine, Tier 1.
   (Today these don't compile at all, so this is technically *new*, just not decomposed.)

## 15. Risks and open questions

| # | Risk / question | Mitigation / decision needed |
|---|---|---|
| R1 | Closure layout is a compiler implementation detail (display-class chaining, field naming for deconstructed tuples). | Isolated in `ClosureReader`; dedicated shape test suite run per SDK in CI; degrade to Tier 1 on lookup miss rather than throw. |
| R2 | Interceptors' official support status / Roslyn floor. | Phase 0 spike: confirm `InterceptorsNamespaces` + v1 checksum format stability on .NET 9/10 SDK; decide floor. |
| R3 | Generic call sites (`Check<T>` helpers) need generic interceptors. | Phase 0 spike; degrade to Tier 1 where unsupported. |
| R4 | Compile-time cost on large test suites. | Per-file emission, equatable models, CI perf budget. No opt-out switch: source generation is the product, not a mode — the eventual replacement of the Expression API depends on it being always-on, so cost problems must be fixed, not toggled away. |
| R5 | Debugging UX inside generated code (breakpoints in asserts, EnC invalidating checksums). | `[StackTraceHidden]`; document; verify EnC behavior in Phase 0. |
| R6 | `&&` leaves sharing state (pattern variables, `out` vars crossing leaf boundaries). | Lowering keeps leaf temps in one scope (no artificial blocks); corpus tests. |
| O1 | Ship a transition release (old API + re-based engine) before the break? | Recommended: yes — it's Phase 1's natural artifact. |
| O2 | Keep the expression adapter shipping permanently (hybrid) or test-only? | Default: test-only oracle; revisit if wrapper-method regression (§14.2) generates real demand. |
| O3 | UnsafeAccessor vs cached reflection for private `this` members. | Decide in Phase 3; reflection is the simple correct start. |

## 16. Implementation phases

| Phase | Work | Exit criterion |
|---|---|---|
| **0. Spikes** (small) | R2/R3/R5 verification; Roslyn floor; generic interceptors; EnC | Go/no-go facts documented |
| **0.5. Vertical slice — EqualsPattern via interceptors** (go/no-go gate, see §16.1) | Slice generator intercepting the *unchanged* `Expression`-based API at whitelisted equality call sites; real packaging; run against `Assertive.Test` + a large real-world suite | Differential parity on equality corpus; R1/R4/R5 validated on real code; **explicit go/no-go decision** |
| **1. Engine re-base** (largest, zero-risk) | `EvaluatedNode`; port 18+10 patterns, formatter, DSL; expression adapter | Existing suite green on old API via adapter |
| **2. Generator core** | Pipeline, interception, manifest, closure reader, leaf lowering (comparisons, boolean, negation, `&&`/`&`/`||`/`|`), node emission, Tier 1/2 | Differential harness green on comparison/boolean corpus |
| **3. Full lowering** | Member chains + step tracking, exception attribution, conditional access, `this`/private access, `Throws` lifting | Exception-pattern corpus green |
| **4. Collection patterns** | `All`/`Any` per-item loops, item context | Collection corpus green |
| **5. Productization** | Packaging/props, diagnostics, perf budget, docs, migration guide, transition release + vNext | Differential harness green on full corpus; all adapter test projects green |

Phase 1 is independently valuable and ships as a no-change release; each later phase is
gated by the differential harness, so parity is enforced continuously rather than audited
at the end.

### 16.1 The Phase 0.5 vertical slice

Because interceptors substitute the method while receiving the original arguments, a probe
does **not** require the API break: the slice generator intercepts the existing
`Assert.That(Expression<Func<bool>>)` and opts in per call site via a conservative
whitelist.

- **Whitelisted shapes** (only those that route to `EqualsPattern`/`NotEqualsPattern`
  today: equality comparisons — not null comparisons, not `Length`/`Count` equality — and
  `&&` chains thereof): the interceptor runs generated decomposed evaluation (single
  evaluation, captured operand values, string diff) and never compiles the tree. The tree
  is still allocated at the call site (signature unchanged) but unused on success.
- **Any exception during generated evaluation**: caught and delegated, tree in hand, to
  the unchanged `FailedAssertionExceptionProvider` — exception-message parity without
  step tracking.
- **Every other call site**: not intercepted; behavior byte-for-byte identical to today.

The slice validates the riskiest unknowns on real code *before* the Phase 1 investment:
packaging (analyzer + buildTransitive props in the real nupkg), closure shapes in the wild
(R1), compile-time cost and incrementality (R4), IDE/EnC behavior (R5), and message parity
for the highest-traffic pattern. It intentionally does not validate the API break, new-
syntax support, or full success-path perf (tree allocation remains; the dominant
`Compile`/interpret cost already disappears). Scaffolding cost is small and acknowledged:
the tree-fallback shims are transitional and deleted in Phase 3.

## 17. References

- `poc/` — proof of concept: interceptor pipeline, closure capture, `&&` splitting,
  comparison decomposition, AnyPattern port, graceful fallback.
- Current pipeline: `AssertImpl.cs`, `Analyzers/AssertionTreeProvider.cs`,
  `Analyzers/AssertionTreeExecutor.cs`, `Analyzers/FriendlyMessageProvider.cs`,
  `Patterns/*`, `ExceptionPatterns/*`, `Plugin/*`.
- Prior art: Swift Testing `#expect` macros, power-assert (JS Babel transform), Rust
  `assert!` diagnostics, ASP.NET Core request-delegate/config-binder interceptor
  generators.
