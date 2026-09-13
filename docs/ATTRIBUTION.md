# Attribution and licensing status

qexec retains the Skua repository history and builds on the maintained `auqw/Skua` core (the local port started from `e069dc1`). Earlier origins include BrenoHenrike/Skua and rodit/RBot. The original README is preserved in [SKUA-UPSTREAM-README.md](SKUA-UPSTREAM-README.md).

Existing notices in source files and bundled third-party tooling remain in place. No top-level license for the inherited application was found in the imported working tree. This repository does not replace those terms or assert that all upstream code is available under a new license. Clarifying the upstream licensing situation is a release task.

The compatibility CoreStory source and LordOfOrder test fixture originate from the user's Skua script collection; original file headers are preserved. The CoreBots compatibility patch applies to an externally supplied collection.

AQWorlds, Artix names, and in-game assets belong to their respective owners. The Flash plugin is obtained through an existing Artix Game Launcher installation and is not redistributed here.

The images in `docs/images` were generated as UI design concepts during qexec development. They illustrate direction rather than actual gameplay, accurate game data, or delivered feature coverage.

For this public import, `Skua.App.WPF/Properties/launchSettings.json` was removed from the entire published history after GitHub push protection found a credential in an old upstream commit. The remaining authorship and history are preserved, with affected commit IDs rewritten.
