# Architecture decision records

An architecture decision record (ADR) captures one significant, hard-to-reverse decision: its context, the options considered, the choice made, and when to revisit it. The [architecture](../ARCHITECTURE.md) and other guides describe how the system works today; ADRs explain why it works that way.

## Conventions

Name each record `NNNN-short-title.md`, numbering sequentially from `0001`. Open each record with a field table holding the status, date, issue, and deciders, and give every record a decision, context, options or alternatives, consequences or risks, and the triggers for revisiting it.

An ADR has one of three statuses:
- **Proposed** while under review.
- **Accepted** once the named owners agree.
- **Superseded by NNNN** when a later record replaces it.

Do not rewrite an accepted decision. Add a new record that supersedes it and update the old record's status line. Correcting facts, links, or prices without changing the decision is fine; note the correction date.

## Records

| ADR | Title | Status |
|---|---|---|
| [0001](0001-identity-provider-build-vs-buy.md) | Retain first-party identity rather than adopt a managed provider | Accepted |
