# Contributing to qexec

Start with the [roadmap](docs/ROADMAP.md). Keep fixes small enough to reproduce and verify. Preserve compatibility with existing Skua scripts unless a change includes a migration plan.

For a bug, include the app revision, OS, script/quest/item ID, expected behavior, actual behavior, and a minimal diagnostic excerpt. Remove account details and credentials. For acquisition routes, include the source and explain how IDs and prerequisites were verified.

Run the checks in the README that match the change. New execution logic should have a test for success, failure, cancellation, and stale state where applicable. Use simulated game responses for repeatable tests; describe any additional live testing separately.

Do not commit settings, credentials, packet dumps, downloaded runtimes, compiled caches, or personal generated scripts. Do not replace a running app's engine binaries: exit the app before installing a build.

PRs should explain the user-visible change, what was tested, and remaining limitations. Keep upstream credits and notices intact.
