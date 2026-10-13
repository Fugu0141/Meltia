# Issue #28 — empty insertion must not invalidate session language locks

- Issue: https://github.com/Fugu0141/Meltia/issues/28
- PR: https://github.com/Fugu0141/Meltia/pull/29
- Base `main` at branch creation: `98159a33a2dcbf5d856ffdb39cbaa611fc416156`
- Scope: research-only `MeltiaSession` and the existing `Meltia.Core.Tests` contract harness.
- Date: 2026-10-14 (JST)

## Reproduction (RED)

Before the fix, `Insert("")` runs `Raw.Insert(CaretUtf16, value)`, increments the caret by zero,
then calls `InvalidateLocks()`. Consequently, text and caret remain unchanged but explicit English
and Japanese locks are cleared. In contrast, `Backspace()` at the start and `DeleteForward()`
at the end return without clearing locks.

Tests were added **before** modifying the production method:

- Test-only commit: `2bf65caf3c6a0f27757c5037df60a6bfab0977ff`
- [RED GitHub Actions run](https://github.com/Fugu0141/Meltia/actions/runs/38421656689)
- .NET 10 Core and CLI build: **success**, zero warnings/errors.
- Boundary contract execution: **failed at the expected issue #28 test**:
  `FAILED: issue #28 empty insert at middle preserves English lock, raw and caret`
- Earlier existing contracts and the English-lock precondition passed, demonstrating a meaningful
  reproduction of the reported regression rather than a compile or test-setup failure.

## Fix and design decision

In `src/Meltia.Core/Boundary/MeltiaSession.cs`, return early when `value.Length == 0`,
**after** the existing null check and **before** mutation or lock invalidation.

This is intentionally limited to the no-op contract. Nonempty insertions still update `Raw`,
advance the UTF-16 caret, enforce input length, and invalidate all explicit language locks as before.
The boundary candidate generator, scoring, dictionaries, native Meltype/TSF, and other edit methods
were not changed. `Insert(null)` continues to throw.

- Source fix commit: `00170c94c224f9fd942b6be30d0ff4272a648190`

## Verification (GREEN)

- [GREEN GitHub Actions Core run](https://github.com/Fugu0141/Meltia/actions/runs/38421808724)
- .NET 10 independent Core and CLI build: **success**.
- `dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release`:
  **ALL PASS 127**, including the 15 Issue #28 contract checks.
- CLI smoke (non-committing): **success**.

The additional tests, in `tests/Meltia.Core.Tests/Issue28SessionNoOpTests.cs`, cover:

| Scenario | Expected outcome |
| --- | --- |
| English lock on ambiguous `tokyo` | Full analysis, text and caret unchanged after empty insert in the middle |
| Japanese lock on `API` | Full analysis unchanged after empty inserts at start and end |
| Nonempty insertion with either lock | Results match a fresh analysis **without** explicit locks |
| MoveCaret; Backspace at start; DeleteForward at end | No-op edit paths retain locks |
| Empty insert adjacent to a supplementary character | UTF-16 caret and analysis unchanged |
| Caret inside a surrogate pair | Still rejected |
| Supplementary-character insert, Backspace, DeleteForward | UTF-16 positions remain valid; real edits clear locks |
| `Insert(null)` | Still throws `ArgumentNullException` and preserves session state |

Analysis equivalence compares the raw text, candidate sequences, ordered route spans and scores,
truncation, auto-commit contract, and uncertainty-review fields (when present). The test also
checks each lock's baseline differs from an unlocked analysis so a lock-loss regression is observable.

## Reproduce locally

```sh
dotnet build src/Meltia.Cli/Meltia.Cli.csproj -c Release --nologo
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project src/Meltia.Cli/Meltia.Cli.csproj -c Release --no-build -- handanwodelegatesuru --word=delegate
```

These tests validate the research session, **not** live Windows keyboard input or TSF.
No user-facing input accuracy claims are made. Keep PR #29 unmerged until independent review.
