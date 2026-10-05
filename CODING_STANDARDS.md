# Coding standards

Read during review. The generic smells (duplication, speculative generality, and so on) come from `/code-review`'s baseline; this file holds only this repo's conventions.

## Comments state the invariant

A comment in `src/` says what holds and why, as true today. It cites its source:

- A decision with alternatives points to its ADR: `(ADR-0004)`.
- A claim resting on an in-game measurement cites the archived ticket by path: `<c>.scratch/ffxiv-ime-bridge/issues/03-keyboard-capture-no-double-input.md</c>`.

Flag history narration ("used to wait", "round 4", "the in-game check asked for it"), bare ticket or milestone references ("ticket 14", "M1"), and changelog-style summaries in doc comments. That history belongs in the commit message.
