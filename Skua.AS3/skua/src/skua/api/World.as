package skua.api {
import flash.display.DisplayObject;
import flash.events.TimerEvent;
import flash.utils.getQualifiedClassName;
import flash.utils.Timer;
import flash.utils.ByteArray;

import skua.Main;

public class World {
    private static var _fxStore:Object = {};
    private static var _fxLastOpt:Boolean = false;
    private static var _jumpCorrectionTimer:Timer;
    private static var _jumpCorrectionHandler:Function;
    private static const PAD_CLASS_REGEX:RegExp = /::Pad_\d+$/;
    private static const JUMP_CORRECTION_INTERVAL:int = 50;
    private static const JUMP_CORRECTION_ATTEMPTS:int = 40;

    private static var mapBytes:ByteArray;
    private static var mapBytesMap:Object;
    private static var mapBytesToken:uint = 0;

    public static function beginMapBytes():String {
        mapBytes = null;
        var world:* = Main.instance.game.world;
        if (world == null || world.map == null || world.mapLoadInProgress)
            throw new Error("Wait for the current map to finish loading.");
        var bytes:ByteArray = world.map.loaderInfo.bytes;
        if (bytes == null || bytes.length < 8 || bytes.length > 30000000)
            throw new Error("Loaded map bytes are unavailable or exceed the discovery limit.");
        mapBytes = bytes;
        mapBytesMap = world.map;
        ++mapBytesToken;
        return JSON.stringify({token:mapBytesToken, length:bytes.length, map:String(world.strMapName), url:String(world.map.loaderInfo.url)});
    }

    public static function readMapBytes(token:uint, offset:uint, count:uint):String {
        if (mapBytes == null || token != mapBytesToken || Main.instance.game.world.map !== mapBytesMap)
            throw new Error("Map changed during discovery. Retry in the new area.");
        if (count == 0 || count > 65536 || offset > mapBytes.length || count > mapBytes.length - offset)
            throw new Error("Invalid map byte range.");
        var hex:String = "0123456789abcdef";
        var chunks:Array = [];
        var previous:uint = mapBytes.position;
        mapBytes.position = offset;
        for (var i:uint = 0; i < count; i++) {
            var b:uint = mapBytes.readUnsignedByte();
            chunks.push(hex.charAt(b >> 4) + hex.charAt(b & 15));
        }
        mapBytes.position = previous;
        return chunks.join("");
    }

    public static function endMapBytes(token:uint):void {
        if (token == mapBytesToken) {mapBytes = null; mapBytesMap = null;}
    }

    public function World() {
        super();
    }

    public static function jumpCorrectRoom(cell:String, pad:String, autoCorrect:Boolean = true, clientOnly:Boolean = false):void {
        var world:* = Main.instance.game.world;
        stopJumpCorrection();
        world.moveToCell(cell, pad, clientOnly);

        if (!autoCorrect)
            return;

        var mapName:String = world.strMapName;
        var map:Object = world.map;
        var timer:Timer = new Timer(JUMP_CORRECTION_INTERVAL, JUMP_CORRECTION_ATTEMPTS);
        _jumpCorrectionTimer = timer;
        _jumpCorrectionHandler = onJumpCorrectionTimer;
        timer.addEventListener(TimerEvent.TIMER, onJumpCorrectionTimer);
        timer.start();

        function onJumpCorrectionTimer(e:TimerEvent):void {
            if (_jumpCorrectionTimer !== timer) {
                timer.stop();
                timer.removeEventListener(TimerEvent.TIMER, onJumpCorrectionTimer);
                return;
            }

            var currentWorld:* = Main.instance.game.world;
            if (currentWorld !== world || currentWorld.strMapName !== mapName || currentWorld.map !== map) {
                stopJumpCorrection();
                return;
            }

            if (map.currentLabel !== cell || map.isPlaying) {
                if (timer.currentCount >= timer.repeatCount)
                    stopJumpCorrection();
                return;
            }

            var validPads:Array = getValidCellPads();
            if (validPads.length == 0) {
                if (timer.currentCount >= timer.repeatCount)
                    stopJumpCorrection();
                return;
            }

            var selectedPad:String;
            if (validPads.indexOf(pad) >= 0)
                selectedPad = pad;
            else if (validPads.indexOf("Left") >= 0)
                selectedPad = "Left";
            else
                selectedPad = validPads[0];

            stopJumpCorrection();
            currentWorld.moveToCell(cell, selectedPad, clientOnly);
        }
    }

    private static function stopJumpCorrection():void {
        if (_jumpCorrectionTimer != null) {
            _jumpCorrectionTimer.stop();
            if (_jumpCorrectionHandler != null)
                _jumpCorrectionTimer.removeEventListener(TimerEvent.TIMER, _jumpCorrectionHandler);
        }

        _jumpCorrectionTimer = null;
        _jumpCorrectionHandler = null;
    }

    private static function getValidCellPads():Array {
        var world:* = Main.instance.game.world;
        var validPads:Array = [];
        if (world == null || world.map == null)
            return validPads;

        for (var i:int = 0; i < world.map.numChildren; ++i) {
            var child:DisplayObject = world.map.getChildAt(i);
            var childName:String = child.name;
            if (childName == null || childName.length == 0)
                continue;
            if (!PAD_CLASS_REGEX.test(getQualifiedClassName(child)))
                continue;
            if (!(childName in world.map) || world.map[childName] !== child)
                continue;

            validPads.push(childName);
        }

        return validPads;
    }

    public static function disableDeathAd(enable:Boolean):void {
        Main.instance.game.userPreference.data.bDeathAd = !enable;
    }

    public static function skipCutscenes():void {
        while (Main.instance.game.mcExtSWF.numChildren > 0) {
            Main.instance.game.mcExtSWF.removeChildAt(0);
        }
        Main.instance.game.showInterface();
    }

    public static function hidePlayers(enabled:Boolean):void {
        var world:* = Main.instance.game.world;
        var currentFrame:String = world.strFrame;
        
        for each (var avatar:* in world.avatars) {
            if (avatar != null && avatar.pnm != null && !avatar.isMyAvatar) {
                if (enabled) {
                    avatar.hideMC();
                } else if (avatar.strFrame == currentFrame) {
                    avatar.showMC();
                }
            }
        }
    }

    public static function disableFX(enabled:Boolean):void {
        if (!_fxLastOpt && enabled) {
            _fxStore = {};
        }
        _fxLastOpt = enabled;
        for each (var avatar:* in Main.instance.game.world.avatars) {
            if (enabled) {
                if (avatar.pMC.spFX != null) {
                    _fxStore[avatar.uid] = avatar.rootClass.spFX;
                }
                avatar.rootClass.spFX = null;
            } else {
                avatar.rootClass.spFX = _fxStore[avatar.uid];
            }
        }
    }

    public static function killLag(enable:Boolean):void {
        // Script cleanup can run on the login screen or after disconnection.
        if (!Main.instance.game || !Main.instance.game.world) return;
        Main.instance.game.world.visible = !enable;
        
        if (Main.instance.customBGLagKiller) {
            Main.instance.customBGLagKiller.visible = enable;
        }
    }
}
}
