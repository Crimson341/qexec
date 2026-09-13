# External script compatibility

qexec uses an external compatible Skua script collection; it does not bundle the full collection. Set `SKUA_SCRIPTS_DIR` to its directory when needed. The default desktop integration reuses `~/Documents/Skua/Scripts`.

`CoreBots.mac.patch` records the Mac-specific adaptations to CoreBots. Review and apply it to a matching version of your external CoreBots source; patch context may differ between versions.

`scripts/CoreStory.cs` preserves the tested quest-preload fix from this development snapshot. Back up your collection's CoreStory.cs before adopting it and compare against newer upstream changes. The fix checks missing class/method boundaries before slicing source so optional preloading cannot crash on a negative index. The engine exposes included source to the preload helper.

`../tests/fixtures/LordOfOrder.cs.txt` is a source-parsing regression fixture, not a complete bundled farming bot. It is not executed by the preload test.
