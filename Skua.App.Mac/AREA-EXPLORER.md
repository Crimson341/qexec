# What to do

Area scans reuse source pages and local shop indexes for five minutes. Initial scans do not decompile the map or open every shop; select an unresolved shop or use **Find additional shops** for deeper discovery. Shop browsing reads inventory once and defers bank loading until farm planning.

The area page discovers the current map, its live monsters, wiki quest pages, and shop routes. It refreshes when the player changes maps while the page is visible. Accepted quests remain available through the existing Auto-do flow.

Select a monster to see permanent and temporary drops. Select a shop to load its live item IDs, prices, ownership counts, and merge requirements. Choose a target quantity and select Farm item, Buy item, or Farm & merge to resolve the full material plan and immediately run the generated script. AC items offer Open shop in game without an automatic purchase. Generated scripts are saved under `Scripts/Generated-Area`.

Shop discovery combines literal map/shop calls from the installed script collection with literal shop button calls discovered by decompiling bytes from the map already loaded by the game, avoiding a separate map download. Map decompilation needs Java and the bundled FFDec tools. Dynamic shop IDs and NPC-only external files may still require opening that shop once in the game; the page explains when the ID is unavailable.

The planner supports permanent monster drops, nested merge/shop materials, owned inventory/bank ingredients, and verified quest-reward materials found in the quest catalog. A matching complete farm script is not required. An unsupported ingredient blocks the plan with its name rather than starting an incomplete farm. Quest acceptance still depends on game access and prerequisites; arbitrary story unlock chains are not synthesized.

Merge plans require a verified bank snapshot before consuming resources. Free monster drops can proceed without it. The script rechecks inventory at every step and validates the shop item ID, recipe, and gold price before buying. AC purchases are blocked. Gold, merge materials, and quest turn-in materials are consumed by the selected plan. Stop cancels discovery or stops the active generated script.

Preview quantities on child nodes are per parent purchase/quest turn-in. Repeated farming is bounded to 1,000 purchases or quest attempts per acquisition step. Game, wiki, or shop changes can still require replanning. Generated acquisition and fixture tests do not prove every live quest or shop is accessible.

Area quest lists resolve exact names against the local quest catalog and live quest tree. Open in game displays the game quest panel; Accept quest checks the loaded ID/name and reports whether the game accepted it. Duplicate names retain separate IDs, unknown IDs remain labeled, and stale area selections are rejected. Acceptance does not start farming automatically.
