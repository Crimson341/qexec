# qexec roadmap

We want the user's goal—not a script filename—to be the starting point. The bar is a completed, verified action with an explanation when it cannot be done.

## 1. Make execution dependable

- [ ] Add regression cases for generated shop, quest, and farming scripts, including repeated starts and cold launches.
- [ ] Detect invalid compiled caches and rebuild them without obscuring the original error.
- [ ] Install engine updates only after the running process exits; stage replacements before switching bundles.
- [ ] Standardize cancellation, timeouts, and restoration of lag-killer rendering and combat settings.
- [ ] Produce useful diagnostics with script name, failing step, and recovery action.

**Done when:** the regression suite repeatedly starts/stops representative scripts without binary errors or stuck game rendering; forced failures produce a specific actionable message.

## 2. Finish the whole quest

- [ ] Match accepted quests by ID and resolve every required objective.
- [ ] Support multi-map monster drops, map pickups, purchases, turn-ins, and reward selection.
- [ ] Walk prerequisite chains and detect cycles or missing evidence.
- [ ] Track current objective counts and verify the server accepted the turn-in.
- [ ] Keep daily limits, membership requirements, and unavailable content visible.

**Done when:** a published fixture set covers complete multi-step quest runs; incomplete plans are blocked with the exact unresolved requirement.

## 3. Get the inspected item

- [ ] Resolve item IDs to verified acquisition sources with provenance and freshness.
- [ ] Check inventory and bank before planning and again before spending materials.
- [ ] Expand merge shops into their ingredient dependency plans.
- [ ] Distinguish travel, open shop, farm materials, purchase, and final ownership verification.
- [ ] Show currency cost and require an explicit purchase action when appropriate.

**Done when:** each supported source type ends with verified ownership or a clear blocked state; opening a shop is never reported as obtaining the item.

## 4. Plan around the character

- [ ] Recommend upgrades by role, equipped class, inventory, bank, and access requirements.
- [ ] Rank prerequisites by reuse across several goals.
- [ ] Refresh event availability from attributable sources and flag stale information.
- [ ] Distinguish currently obtainable gear from historical rare entries.
- [ ] Explain why an item is recommended and what the route requires.

**Done when:** every recommendation has a reason, ownership state, evidence-backed availability, and an executable or explicitly blocked plan.

## 5. Recover intelligently

- [ ] Detect no-progress loops, missing monsters, failed map transitions, and disconnects.
- [ ] Retry with limits, alternate verified routes, or pause for the user.
- [ ] Persist checkpoints without account credentials or raw game packets.
- [ ] Expand tested class profiles for farming, bosses, healing, and defensive behavior.

**Done when:** injected failures cannot run indefinitely; resuming rechecks quest state and does not duplicate completed purchases or turn-ins.

## 6. Refine Quest Ledger

- [x] Joined left ledger, top navigation, and visible transport controls.
- [x] Show accepted quests from the game.
- [x] Vibe questing pixels and reduced-motion support.
- [ ] Show per-objective counts and the current generated step directly in the ledger.
- [ ] Make the ledger's quest action open the exact selected quest.
- [ ] Add a script preview and plan explanation before execution.
- [ ] Validate keyboard use, small windows, long item names, and error states.
- [ ] Add an optional compact HUD after the primary layout is reliable.

## Release gates

No dates are promised yet. Before calling a build stable: run automated checks, publish results for representative live quests, document unsupported routes, verify clean installation/update/rollback, review dependency and licensing status, and ship reproducible setup instructions.
