package skua {
import flash.external.ExternalInterface;
import flash.system.Security;
import flash.events.TimerEvent;
import flash.utils.Timer;

import skua.api.Auras;
import skua.api.Combat;
import skua.api.Monster;
import skua.api.Player;
import skua.api.Server;
import skua.api.Shop;
import skua.api.Skills;
import skua.api.World;
import skua.module.Modules;
import skua.remote.RemoteRegistry;

public class Externalizer {
    private var browserBridge:Boolean = false;
    private var callbacks:Object = {};
    private var browserTimer:Timer;

    public function Externalizer() {
        super();
    }

    public function init(root:Main):void {
        browserBridge = root.loaderInfo.parameters.skuaMac == "1";
        // Core initialization
        this.addCallback("loadClient", Main.loadGame);
        this.addCallback("setBackgroundValues", Main.setBackgroundValues);
        this.addCallback("isTrue", Main.isTrue);
        this.addCallback("isNull", Main.isNull);

        // Game object access
        this.addCallback("getGameObject", Main.getGameObject);
        this.addCallback("getGameObjectS", Main.getGameObjectS);
        this.addCallback("getGameObjectKey", Main.getGameObjectKey);
        this.addCallback("setGameObject", Main.setGameObject);
        this.addCallback("setGameObjectKey", Main.setGameObjectKey);
        this.addCallback("getArrayObject", Main.getArrayObject);
        this.addCallback("setArrayObject", Main.setArrayObject);
        this.addCallback("callGameFunction", Main.callGameFunction);
        this.addCallback("callGameFunction0", Main.callGameFunction0);
        this.addCallback("selectArrayObjects", Main.selectArrayObjects);

        // Server
        this.addCallback("connectToServer", Server.connectToServer);
        this.addCallback("clickServer", Server.clickServer);

        // Player
        this.addCallback("isLoggedIn", Player.isLoggedIn);
        this.addCallback("isKicked", Player.isKicked);
        this.addCallback("walkTo", Player.walkTo);
        this.addCallback("untargetSelf", Player.untargetSelf);
        this.addCallback("attackPlayer", Player.attackPlayer);
        this.addCallback("getAvatar", Player.getAvatar);
        this.addCallback("inspectGear", Player.inspectGear);
        this.addCallback("getLoadouts", Player.getLoadouts);
        this.addCallback("gender", Player.Gender);
        this.addCallback("rejectExcept", Player.rejectExcept);


        // World
        this.addCallback("jumpCorrectRoom", World.jumpCorrectRoom);
        this.addCallback("disableDeathAd", World.disableDeathAd);
        this.addCallback("skipCutscenes", World.skipCutscenes);
        this.addCallback("hidePlayers", World.hidePlayers);
        this.addCallback("disableFX", World.disableFX);
        this.addCallback("killLag", World.killLag);

        // Monster
        this.addCallback("availableMonsters", Monster.availableMonstersInCell);
        this.addCallback("getMonsters", Monster.getMonsters);
        this.addCallback("getTargetMonster", Monster.getTargetMonster);
        this.addCallback("attackMonsterName", Monster.attackMonsterByName);
        this.addCallback("attackMonsterID", Monster.attackMonsterByID);

        // Skills
        this.addCallback("canUseSkill", Skills.canUseSkill);
        this.addCallback("useSkill", Skills.useSkill);

        // Combat
        this.addCallback("infiniteRange", Combat.infiniteRange);
        this.addCallback("magnetize", Combat.magnetize);

        // Shop
        this.addCallback("buyItemByName", Shop.buyItemByName);
        this.addCallback("buyItemByID", Shop.buyItemByID);
        this.addCallback("getShopItem", Shop.getShopItem);
        this.addCallback("getShopItemByID", Shop.getShopItemByID);

        // Auras
        this.addCallback("getSubjectAuras", Auras.getSubjectAuras);
        this.addCallback("HasAnyActiveAura", Auras.HasAnyActiveAura);
        this.addCallback("GetPlayerAura", Auras.GetPlayerAura);
        this.addCallback("GetMonsterAuraByName", Auras.GetMonsterAuraByName);
        this.addCallback("GetMonsterAuraByID", Auras.GetMonsterAuraByID);
        this.addCallback("rebuildAuraArray", Auras.rebuildAuraArray);
        this.addCallback("GetAurasValue", Auras.GetAurasValue);
        this.addCallback("rebuilduoTree", Auras.rebuilduoTree);
        this.addCallback("rebuildmonTree", Auras.rebuildmonTree);

        // Packets
        this.addCallback("sendClientPacket", Main.sendClientPacket);
        this.addCallback("catchPackets", Main.catchPackets);

        // Utilities
        this.addCallback("injectScript", Main.injectScript);

        // Remote Registry
        this.addCallback("lnkCreate", RemoteRegistry.ext_create);
        this.addCallback("lnkDestroy", RemoteRegistry.ext_destroy);
        this.addCallback("lnkGetChild", RemoteRegistry.ext_getChild);
        this.addCallback("lnkDeleteChild", RemoteRegistry.ext_deleteChild);
        this.addCallback("lnkGetValue", RemoteRegistry.ext_getValue);
        this.addCallback("lnkSetValue", RemoteRegistry.ext_setValue);
        this.addCallback("lnkCall", RemoteRegistry.ext_call);
        this.addCallback("lnkGetArray", RemoteRegistry.ext_getArray);
        this.addCallback("lnkSetArray", RemoteRegistry.ext_setArray);

        // Function Call Registry
        this.addCallback("fcCreate", RemoteRegistry.ext_fcCreate);
        this.addCallback("fcPush", RemoteRegistry.ext_fcPushArgs);
        this.addCallback("fcPushDirect", RemoteRegistry.ext_fcPushArgsDirect);
        this.addCallback("fcClear", RemoteRegistry.ext_fcClearArgs);
        this.addCallback("fcCallFlash", RemoteRegistry.ext_fcCallFlash);
        this.addCallback("fcCall", RemoteRegistry.ext_fcCall);

        // Modules
        this.addCallback("modEnable", Modules.enable);
        this.addCallback("modDisable", Modules.disable);

		this.debug("Externalizer::init done.");
        if (browserBridge) this.debug("Flash sandbox: " + Security.sandboxType);
        if (browserBridge) {
            browserTimer = new Timer(10);
            browserTimer.addEventListener(TimerEvent.TIMER, pollBrowserCommands);
            browserTimer.start();
        }
        this.call("requestLoadGame");
    }

    public function addCallback(name:String, func:Function):void {
        callbacks[name] = func;
        ExternalInterface.addCallback(name, func);
    }

    private function pollBrowserCommands(event:TimerEvent):void {
        var json:String = ExternalInterface.call("skuaNextFlashCommands");
        if (!json || json == "[]") return;
        var commands:Array = JSON.parse(json) as Array;
        for each (var command:Object in commands) {
            var result:* = null;
            var error:String = null;
            try {
                var callback:Function = callbacks[command["function"]] as Function;
                if (callback == null) throw new Error("Unknown Flash callback: " + command["function"]);
                result = callback.apply(null, command.args);
                if (result === undefined) result = null;
            } catch (failure:Error) {
                error = failure.toString();
            }
            ExternalInterface.call("skuaFlashReply", command.id, result, error);
        }
    }

    public function call(name:String, ...rest):* {
        if (browserBridge) return ExternalInterface.call("skuaFlashEvent", name, rest);
        return ExternalInterface.call(name, rest);
    }

    public function debug(message:String):void {
        this.call("debug", message);
    }
}
}
