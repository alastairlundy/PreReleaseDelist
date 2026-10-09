---
title: Glossary sync verification
classification: Independent
blocked_by: []
parent: IMPLEMENTATION-prerelease-delist-1.0-readiness.md
---

# TK016 — Glossary sync verification

## Goal

Verify that `GLOSSARY.md` carries exactly the six terms and wordings that decision record D003 fixed, closing the one ledger record whose implementation already landed before ticketing - the user chose verification-only over re-implementation.

## What to build

A verification-only pass (no file changes expected): confirm `GLOSSARY.md` carries - *Package delisting*, *Delist planning*, *Delete backend*, *Delist plan*, *Version outcome*, and *Dry run* - and that each definition matches the wording the Decision Ledger records under D003 (the ledger's outcome vocabulary from ticket 002's enum, the delist-plan buckets, and the dry-run contract must match the glossary's wording). Constraints: the glossary stays implementation-free (no code identifiers), and any future term change goes through term resolution rather than silent edits - if any wording mismatch surfaces, stop and raise it instead of editing the glossary.

## Recommended Workflow

### Step 1 - Compare glossary against the ledger record

Where: `GLOSSARY.md`, `docs/decisions/DECISIONS-prerelease-delist-1.0-readiness.md`

- Read the D003 record and the six glossary entries; compare definitions term by term.
- Check the terms are implementation-free (no code identifiers in the definitions).

Verify: all six terms match the D003 wording, or a mismatch is reported rather than silently fixed.

## Context pointers

##### Files

- `GLOSSARY.md` - the six terms under verification.

##### ADRs

None - the `docs/adr/` directory does not exist; the Decision Ledger is the only decision source for this feature.

##### Domain terms

All six glossary terms are the subject of this ticket - no additional terms needed.

##### Ledger records

- `DECISIONS-prerelease-delist-1.0-readiness.md#D003` - the glossary terms this ticket verifies against the file.
- `DECISIONS-prerelease-delist-1.0-readiness.md#T004` - the outcome vocabulary whose wording D003 pins to the glossary.
- `DECISIONS-prerelease-delist-1.0-readiness.md#T003` - the dry-run contract wording pinned to the glossary.
- `DECISIONS-prerelease-delist-1.0-readiness.md#T001` - the seam decision that made the original "Delete backend" aspiration literal.

## Acceptance criteria

- [x] All six D003 terms exist in `GLOSSARY.md` with matching definitions
- [x] Definitions contain no code identifiers
- [x] Any wording mismatch is reported, not silently edited
