package skua {
import flash.display.DisplayObject;
import flash.display.Loader;
import flash.display.LoaderInfo;
import flash.display.MovieClip;
import flash.display.Stage;
import flash.display.StageAlign;
import flash.display.StageScaleMode;
import flash.events.Event;
import flash.events.KeyboardEvent;
import flash.events.MouseEvent;
import flash.events.TimerEvent;
import flash.net.URLLoader;
import flash.net.URLRequest;
import flash.system.ApplicationDomain;
import flash.system.LoaderContext;
import flash.system.Security;
import flash.text.TextField;
import flash.utils.Timer;
import flash.utils.getQualifiedClassName;

import skua.module.ModalMC;
import skua.module.Modules;
import skua.util.SFSEvent;

[SWF(frameRate="30", backgroundColor="#000000", width="958", height="550")]
public class Main extends MovieClip {
    public static var instance:Main;
    private static var _gameClass:Class;
    private static var _fxStore:Object = {};
    private static var _fxLastOpt:Boolean = false;
    private static var _handler:*;
    private static const PAD_NAMES_REGEX:RegExp = /(Spawn|Center|Left|Right|Up|Down|Top|Bottom)/;
    private static const DROP_PARSE_REGEX:RegExp = /(.*)\s+x\s*(\d*)/g;

    private var game:*;
    private var external:Externalizer;
    private var sURL:String = 'https://game.aq.com/game/';
    private var versionUrl:String = (sURL + 'api/data/gameversion');
    private var loginURL:String = (sURL + 'api/login/now');
    private var sFile:String;
    private var sBG:String = 'hideme.swf';
    private var isEU:Boolean;
    private var urlLoader:URLLoader;
    private var vars:Object;
    private var loader:Loader;
    private var sTitle:String = '<font color="#FDAF2D">AURAS!!!</font>';
    private var stg:Stage;
    private var gameDomain:ApplicationDomain;
    private var customBGLoader:Loader;
    private var customBGReady:MovieClip = null;
    private var customBGLagKiller:MovieClip = null;
    private var customBackgroundURL:String;

    public function Main() {
        String.prototype.trim = function():String {
            var s:String = String(this);
            return s.replace(/^\s+|\s+$/g, "");
        };

        Main.instance = this;

        if (stage) this.init();
        else addEventListener(Event.ADDED_TO_STAGE, this.init);
    }

    public static function loadGame():void {
        Main.instance.onAddedToStage();
        Main.instance.external.call('pre-load');
    }

    public static function setBackgroundValues(sBGValue:String, customBackground:String):void {
        if (sBGValue && sBGValue.length > 0) {
            instance.sBG = sBGValue;
            if (instance.game && instance.game.params) {
                instance.game.params.sBG = sBGValue;
            }
        }
        if (customBackground && customBackground.length > 0) {
            instance.customBackgroundURL = customBackground;
            instance.initCustomBackground();
        } else {
            instance.customBackgroundURL = null;
        }
    }

    private function init(e:Event = null):void {
        removeEventListener(Event.ADDED_TO_STAGE, this.init);
        this.external = new Externalizer();
        this.external.init(this);
    }

    private function onAddedToStage():void {
        Security.allowDomain('*');
        this.urlLoader = new URLLoader();
        this.urlLoader.addEventListener(Event.COMPLETE, this.onDataComplete);
        this.urlLoader.load(new URLRequest(this.versionUrl));
    }

    private function onDataComplete(event:Event):void {
        this.urlLoader.removeEventListener(Event.COMPLETE, this.onDataComplete);
        this.vars = JSON.parse(event.target.data);
        this.sFile = ((this.vars.sFile + '?ver=') + Math.random());
        this.loadGame()
    }

    private function loadGame():void {
        this.loader = new Loader();
        this.loader.contentLoaderInfo.addEventListener(Event.COMPLETE, this.onComplete);
        this.loader.load(new URLRequest(this.sURL + 'gamefiles/' + this.sFile));
    }

    private function onComplete(event:Event):void {
        this.loader.contentLoaderInfo.removeEventListener(Event.COMPLETE, this.onComplete);

        this.stg = stage;
        this.stg.removeChildAt(0);
        this.game = this.stg.addChild(this.loader.content);
        this.stg.scaleMode = StageScaleMode.SHOW_ALL;
        this.stg.align = StageAlign.TOP;

        for (var param:String in root.loaderInfo.parameters) {
            this.game.params[param] = root.loaderInfo.parameters[param];
        }

        this.game.params.vars = this.vars;
        this.game.params.sURL = this.sURL;
        this.game.params.sBG = this.sBG;
        this.game.params.sTitle = this.sTitle;
        this.game.params.isEU = this.isEU;
        this.game.params.loginURL = this.loginURL;

        this.game.addEventListener(MouseEvent.CLICK,this.onGameClick);
        this.game.sfc.addEventListener(SFSEvent.onExtensionResponse, this.onExtensionResponse);
        this.gameDomain = LoaderInfo(event.target).applicationDomain;

        Modules.init();
        this.stg.addEventListener(Event.ENTER_FRAME, Modules.handleFrame);
        this.stg.addEventListener(Event.ENTER_FRAME, this.monitorLoginScreen);

        this.game.stage.addEventListener(KeyboardEvent.KEY_DOWN, this.key_StageGame);
        
        if (this.customBackgroundURL && this.customBackgroundURL.length > 0) {
            this.initCustomBackground();
        }
        
        this.external.call('loaded');
    }

    public function onExtensionResponse(packet:*):void {
        this.external.call('pext', JSON.stringify(packet));
    }

    private function onGameClick(event:MouseEvent) : void
    {
        if (event == null)
                return;
        var className:String = getQualifiedClassName(event.target.parent);
        switch(event.target.name)
        {
            case "btCharPage":
                this.external.call("openWebsite","https://account.aq.com/CharPage?id=" + event.target.parent.txtUserName.text);
                return;
            case "btnWiki":
                if (event.target.parent.parent.parent.name == "qRewardPrev") {
                    this.external.call("openWebsite", "https://aqwwiki.wikidot.com/" + instance.game.ui.getChildByName("qRewardPrev").cnt.strTitle.text);
                } else if (className.indexOf("LPFFrameItemPreview") > -1) {
                    this.external.call("openWebsite","https://aqwwiki.wikidot.com/" + event.target.parent.tInfo.getLineText(0));
                } else if (className.indexOf("LPFFrameHousePreview") > -1) {
                    this.external.call("openWebsite","https://aqwwiki.wikidot.com/" + instance.game.ui.mcPopup.getChildByName("mcInventory").previewPanel.frames[3].mc.tInfo.getLineText(0));
                } else if (className.indexOf("mcQFrame") > -1) {
                    this.external.call("openWebsite","https://cse.google.com/cse?oe=utf8&ie=utf8&source=uds&safe=active&sort=&cx=015511893259151479029:wctfduricyy&start=0#gsc.tab=0&gsc.q=" + instance.game.getInstanceFromModalStack("QFrameMC").qData.sName);
                }
                return;
            case "hit":
                if (className.indexOf("cProto") > -1 && event.target.parent.ti.text.toLowerCase() == "wiki monster") {
                    this.external.call("openWebsite", "https://aqwwiki.wikidot.com/" + instance.game.world.myAvatar.target.objData.strMonName || "monsters");
                }
                return;
            default:
                return;
        }
    }

    private function monitorLoginScreen(event:Event):void {
        if (!this.customBGReady || !this.game || !this.game.mcLogin) return;
        
        if (this.game.mcLogin.visible && this.game.mcLogin.mcTitle) {
            var hasCustomBG:Boolean = false;
            var numChildren:int = this.game.mcLogin.mcTitle.numChildren;
            
            for (var i:int = 0; i < numChildren; i++) {
                if (this.game.mcLogin.mcTitle.getChildAt(i) == this.customBGReady) {
                    hasCustomBG = true;
                    break;
                }
            }
            
            if (!hasCustomBG) {
                if (this.customBGReady.parent) {
                    this.customBGReady.parent.removeChild(this.customBGReady);
                }
                while (this.game.mcLogin.mcTitle.numChildren > 0) {
                    this.game.mcLogin.mcTitle.removeChildAt(0);
                }
                this.game.mcLogin.mcTitle.addChild(this.customBGReady);
            }
        }
    }

    private function initCustomBackground():void {
        if (!this.customBackgroundURL) {
            return;
        }

        this.customBGLoader = new Loader();
        this.customBGLoader.contentLoaderInfo.addEventListener(Event.COMPLETE, function (e:Event):void {
            customBGReady = MovieClip(customBGLoader.content);

            var checkTimer:Timer = new Timer(100);
            checkTimer.addEventListener(TimerEvent.TIMER, function (timerEvent:TimerEvent):void {
                if (game) {
                    while (game.mcLogin.mcTitle.numChildren > 0) {
                        game.mcLogin.mcTitle.removeChildAt(0);
                    }
                    game.mcLogin.mcTitle.addChild(customBGReady);
                    checkTimer.stop();
                }
            });
            checkTimer.start();
        });
        this.customBGLoader.load(new URLRequest(this.customBackgroundURL));
        
        var lagKillerLoader:Loader = new Loader();
        lagKillerLoader.contentLoaderInfo.addEventListener(Event.COMPLETE, function (e:Event):void {
            customBGLagKiller = MovieClip(lagKillerLoader.content);
            if (game) {
                game.addChildAt(customBGLagKiller, 0);
                customBGLagKiller.visible = false;
            }
        });
        lagKillerLoader.load(new URLRequest(this.customBackgroundURL));
    }

    public function key_StageGame(kbArgs:KeyboardEvent):void {
        if (!(kbArgs.target is TextField || kbArgs.currentTarget is TextField)) {
            if (kbArgs.keyCode == this.game.litePreference.data.keys['Bank']) {
                if (this.game.stage.focus == null || (this.game.stage.focus != null && !('text' in this.game.stage.focus))) {
                    this.game.world.toggleBank();
                }
            }
        }
    }

    public function getGame():* {
        return this.game;
    }

    public function getExternal():Externalizer {
        return this.external;
    }

    public static function getGameObject(path:String):String {
        var obj:* = _getObjectS(instance.game, path);
        return JSON.stringify(obj);
    }

    public static function jumpCorrectRoom(cell:String, pad:String, autoCorrect:Boolean = true, clientOnly:Boolean = false):void {
        var world:* = instance.game.world;
        
        if (!autoCorrect) {
            world.moveToCell(cell, pad, clientOnly);
        } else {
            var users:Array = world.areaUsers;
            users.splice(users.indexOf(instance.game.sfc.myUserName), 1);
            users.sort();
            if (users.length <= 1) {
                world.moveToCell(cell, pad, clientOnly);
            } else {
                var uoTree:* = world.uoTree;
                var usersCell:String = world.strFrame;
                var usersPad:String = "Left";
                for (var i:int = 0; i < users.length; i++) {
                    var userObj:* = uoTree[users[i]];
                    usersCell = userObj.strFrame;
                    usersPad = userObj.strPad;
                    if (cell == usersCell && pad != usersPad)
                        break;
                }
                world.moveToCell(cell, usersPad, clientOnly);
            }

            var jumpTimer:Timer = new Timer(50, 1);
            jumpTimer.addEventListener(TimerEvent.TIMER, jumpTimerEvent);
            jumpTimer.start();

            function jumpTimerEvent(e:TimerEvent):void {
                jumpCorrectPad(cell, clientOnly);
                jumpTimer.stop();
                jumpTimer.removeEventListener(TimerEvent.TIMER, jumpTimerEvent);
            }
        }
    }

    public static function jumpCorrectPad(cell:String, clientOnly:Boolean = false):void {
        var cellPad:String = 'Left';
        var padArr:Array = getCellPads();
        var world:* = instance.game.world;
        
        if (padArr.indexOf(cellPad) >= 0) {
            if (world.strPad === cellPad)
                return;
            world.moveToCell(cell, cellPad, clientOnly);
        } else {
            cellPad = padArr[0];
            if (world.strPad === cellPad)
                return;
            world.moveToCell(cell, cellPad, clientOnly);
        }
    }

    public static function getCellPads():Array {
        var cellPads:Array = [];
        var cellPadsCnt:int = instance.game.world.map.numChildren;
        for (var i:int = 0; i < cellPadsCnt; ++i) {
            var child:DisplayObject = instance.game.world.map.getChildAt(i);
            if (PAD_NAMES_REGEX.test(child.name)) {
                cellPads.push(child.name);
            }
        }
        return cellPads;
    }

    private static function getProperties(obj:*):String {
        var p:*;
        var res:String = '';
        var val:String;
        var prop:String;
        for (p in obj) {
            prop = String(p);
            if (prop && prop !== '' && prop !== ' ') {
                val = String(obj[p]);
                res += prop + ': ' + val + ', ';
            }
        }
        res = res.substr(0, res.length - 2);
        return res;
    }

    public static function getGameObjectS(path:String):String {
        if (_gameClass == null) {
            _gameClass = instance.gameDomain.getDefinition(getQualifiedClassName(instance.game)) as Class;
        }
        var obj:* = _getObjectS(_gameClass, path);
        return JSON.stringify(obj);
    }

    public static function getGameObjectKey(path:String, key:String):String {
        var obj:* = _getObjectS(instance.game, path);
        var obj2:* = obj[key];
        return (JSON.stringify(obj2));
    }

    public static function setGameObject(path:String, value:*):void {
        var parts:Array = path.split('.');
        var varName:String = parts.pop();
        var obj:* = _getObjectA(instance.game, parts);
        obj[varName] = value;
    }

    public static function setGameObjectKey(path:String, key:String, value:*):void {
        var parts:Array = path.split('.');
        var obj:* = _getObjectA(instance.game, parts);
        obj[key] = value;
    }

    public static function getArrayObject(path:String, index:int):String {
        var obj:* = _getObjectS(instance.game, path);
        return JSON.stringify(obj[index]);
    }

    public static function setArrayObject(path:String, index:int, value:*):void {
        var obj:* = _getObjectS(instance.game, path);
        obj[index] = value;
    }

    public static function callGameFunction(path:String, ...args):String {
        var parts:Array = path.split('.');
        var funcName:String = parts.pop();
        var obj:* = _getObjectA(instance.game, parts);
        var func:Function = obj[funcName] as Function;
        return JSON.stringify(func.apply(null, args));
    }

    public static function callGameFunction0(path:String):String {
        var parts:Array = path.split('.');
        var funcName:String = parts.pop();
        var obj:* = _getObjectA(instance.game, parts);
        var func:Function = obj[funcName] as Function;
        return JSON.stringify(func.apply());
    }

    public static function selectArrayObjects(path:String, selector:String):String {
        var obj:* = _getObjectS(instance.game, path);
        if (!(obj is Array)) {
            instance.external.debug('selectArrayObjects target is not an array');
            return '';
        }
        var array:Array = obj as Array;
        var nArray:Array = [];
        for (var j:int = 0; j < array.length; j++) {
            nArray.push(_getObjectS(array[j], selector));
        }
        return JSON.stringify(nArray);
    }

    public static function _getObjectS(root:*, path:String):* {
        return _getObjectA(root, path.split('.'));
    }

    public static function _getObjectA(root:*, parts:Array):* {
        var obj:* = root;
        for (var i:int = 0; i < parts.length; i++) {
            obj = obj[parts[i]];
        }
        return obj;
    }

    public static function isNull(path:String):String {
        try {
            return (_getObjectS(instance.game, path) == null).toString();
        } catch (ex:Error) {
        }
        return 'true';
    }

    private static function killLoginModals():void {
        var loc2_:MovieClip = null;
        var loc1_:MovieClip = instance.game.mcLogin.ModalStack;
        var loc3_:int = 0;
        while (loc3_ < loc1_.numChildren) {
            loc2_ = loc1_.getChildAt(loc3_) as MovieClip;
            if ("fClose" in loc2_) {
                loc2_.fClose();
            }
            loc3_++;
        }
    }

    public static function connectToServer(server:String):String {
        var serverData:Object = JSON.parse(server);
        var objLogin:Object = null;

        var connectionServerTimer:Timer = new Timer(500, 50);
        connectionServerTimer.addEventListener(TimerEvent.TIMER, connectingServer);
        connectionServerTimer.start();

        function connectingServer(e:Event):void {
            if (objLogin != null) {
                connectServer(serverData, objLogin);
                connectionServerTimer.stop();
                connectionServerTimer.removeEventListener(TimerEvent.TIMER, connectingServer);
            }
            objLogin = JSON.parse(getGameObjectS("objLogin"));
        }

        return true.toString();
    }

    public function equipLoadout(setName:String, changeColors:Boolean = false): void
    {
        if(!instance.game.world.coolDown("equipLoadout") || setName == null || setName == "")
        {
            return;
        }
        instance.game.sfc.sendXtMessage("zm","equipLoadout",["cmd",setName,!changeColors],"str",instance.game.world.curRoom);
    }

    public function onNewSet() : void
    {
        var curItem:* = undefined;
        var itemsArray:Array = ["he","ba","ar","co","Weapon","pe","am","mi"];
        for each(curItem in itemsArray)
        {
            if(instance.game.world.myAvatar.objData.eqp[curItem] != null)
            {
                instance.game.world.myAvatar.loadMovieAtES(curItem,instance.game.world.myAvatar.objData.eqp[curItem].sFile,instance.game.world.myAvatar.objData.eqp[curItem].sLink);
            }
            else
            {
                instance.game.world.myAvatar.unloadMovieAtES(curItem);
            }
        }
    }

    public static function getLoadouts():String {
        var loadouts:Object = instance.game.world.objInfo["customs"].loadouts;
        return JSON.stringify(loadouts);
    }

    private static function connectServer(server:Object, objLoginData:Object):* {
        var _loc2_:ModalMC = null;
        var _loc3_:Object = null;
        var _loc4_:* = undefined;
        instance.game.showTracking("4");
        if (!instance.game.serialCmdMode) {
            if ((_loc4_ = server).bOnline == 0) {
                instance.game.MsgBox.notify("Server currently offline!");
            } else if (_loc4_.iCount >= _loc4_.iMax) {
                instance.game.MsgBox.notify("Server is Full!");
            } else if (_loc4_.iChat > 0 && objLoginData.bCCOnly == 1) {
                instance.game.MsgBox.notify("Account Restricted to Moglin Sage Server Only.");
            } else if (_loc4_.iChat > 0 && objLoginData.iAge < 13 && objLoginData.iUpgDays < 0) {
                instance.game.MsgBox.notify("Ask your parent to upgrade your account in order to play on chat enabled servers.");
            } else if (_loc4_.bUpg == 1 && objLoginData.iUpgDays < 0) {
                _loc2_ = new ModalMC();
                _loc3_ = {};
                _loc3_.strBody = "Member Server! Do you want to upgrade your account to access this premium server now?";
                _loc3_.params = {};
                _loc3_.glow = "white,medium";
                _loc3_.btns = "dual";
                instance.game.mcLogin.ModalStack.addChild(_loc2_);
                _loc2_.init(_loc3_);
            } else if (Number(_loc4_.iMax) % 2 > 0) {
                _loc2_ = new ModalMC();
                _loc3_ = {};
                _loc3_.strBody = "Testing Server! Do you want to switch to the testing game client?";
                _loc3_.params = {};
                _loc3_.glow = "white,medium";
                _loc3_.btns = "dual";
                instance.game.mcLogin.ModalStack.addChild(_loc2_);
                _loc2_.init(_loc3_);
            } else if (_loc4_.iLevel > 0 && objLoginData.iEmailStatus <= 2) {
                _loc2_ = new ModalMC();
                _loc3_ = {};
                _loc3_.strBody = "This server requires a confirmed email address.";
                _loc3_.params = {};
                _loc3_.glow = "red,medium";
                _loc3_.btns = "mono";
                instance.game.mcLogin.ModalStack.addChild(_loc2_);
                _loc2_.init(_loc3_);
            } else {
                instance.game.objServerInfo = _loc4_;
                instance.game.chatF.iChat = _loc4_.iChat;
                killLoginModals();
                instance.game.connectTo(_loc4_.sIP, _loc4_.iPort);
            }
        }
    }

    public static function isTrue():String {
        return true.toString();
    }

    public static function auraComparison(target:String, operator:String, auraName:String, auraValue:int):String {
        var aura:Object = null;
        var auras:Object = null;
        try {
            auras = target == 'Self' ? instance.game.world.myAvatar.dataLeaf.auras : instance.game.world.myAvatar.target.dataLeaf.auras;
        } catch (e:Error) {
            return false.toString();
        }

        for (var i:int = 0; i < auras.length; i++) {
            aura = auras[i];
            
            if (!aura) {
                continue;
            }
            
            if (!aura.hasOwnProperty("nam") || !aura.nam) {
                continue;
            }
            
            if (aura.e == 1) {
                continue;
            }
            
            if (aura.nam.toLowerCase() == auraName.toLowerCase()) {
                var actualValue:int = (aura.val == undefined || aura.val == null) ? 1 : parseInt(aura.val);
                if (operator == 'Greater' && actualValue > auraValue) {
                    return true.toString();
                }
                if (operator == 'Less' && actualValue < auraValue) {
                    return true.toString();
                }
                if (operator == 'Equal' && actualValue == auraValue) {
                    return true.toString();
                }
                if (operator == 'GreaterOrEqual' && actualValue >= auraValue) {
                    return true.toString();
                }
                if (operator == 'LessOrEqual' && actualValue <= auraValue) {
                    return true.toString();
                }
            }
        }
        return false.toString();
    }

    public static function getSubjectAuras(subject:String):Array {
        if (subject == 'Self')
        {
            var userObj:* = instance.game.world.uoTree[instance.game.sfc.myUserName.toLowerCase()];
            return rebuildAuraArray(userObj.auras)
        }
        else
        {
            var monID:int = 0;
            if (instance.game.world.myAvatar.target != null) {
                monID = instance.game.world.myAvatar.target.dataLeaf.MonMapID;
            }
            var monObj:* = instance.game.world.monTree[monID];
            return rebuildAuraArray(monObj.auras)
        }
    }

    public static function  rebuildAuraArray(auras:Object):Array {
        var rebuiltAuras:Array = [];
        if (!auras) {
            return rebuiltAuras;
        }
        
        if (auras.length > 250) {
            var expiredCount:int = 0;
            for (var j:int = 0; j < auras.length; j++) {
                var checkAura:Object = auras[j];
                if (!checkAura || !checkAura.hasOwnProperty("nam") || !checkAura.nam || checkAura.e == 1) {
                    expiredCount++;
                }
            }
            
            if (expiredCount > auras.length * 0.5) {
                for (var k:int = auras.length - 1; k >= 0; k--) {
                    var cleanAura:Object = auras[k];
                    if (!cleanAura || !cleanAura.hasOwnProperty("nam") || !cleanAura.nam || cleanAura.e == 1) {
                        auras.splice(k, 1);
                    }
                }
            }
        }
        
        for (var i:int = 0; i < auras.length; i++) {
            var aura:Object = auras[i];
            
            if (!aura) {
                continue;
            }
            
            if (!aura.hasOwnProperty("nam") || !aura.nam) {
                continue;
            }
            
            if (aura.e == 1) {
                continue;
            }
            
            var rebuiltAura:Object = {};
            var hasVal:Boolean = false;
            
            for (var key:String in aura) {
                if (key == "cLeaf") {
                    rebuiltAura[key] = "cycle_";
                } else if (key == "val") {
                    var rawVal:* = aura[key];
                    if (rawVal == null || isNaN(rawVal)) {
                        rebuiltAura[key] = 1;
                    } else {
                        rebuiltAura[key] = rawVal;
                    }
                    hasVal = true;
                } else {
                    rebuiltAura[key] = aura[key];
                }
            }
            if (!hasVal) {
                rebuiltAura.val = 1;
            }
            
            rebuiltAuras.push(rebuiltAura);
        }
        
        return rebuiltAuras;
    }

    public static function rebuilduoTree(playerName:String):Object {
        var plrUser:String = playerName.toLowerCase();
        var userObj:* = instance.game.world.uoTree[plrUser];
        if (!userObj) {
            return {};
        }

        var rebuiltObj:Object = {};
        for (var prop:String in userObj) {
            if (prop == "auras") {
                rebuiltObj[prop] = rebuildAuraArray(userObj.auras);
            } else {
                rebuiltObj[prop] = userObj[prop];
            }
        }

        return rebuiltObj;
    }

    public static function rebuildmonTree(monID:int):Object {
        var monObj:* = instance.game.world.monTree[monID];
        if (!monObj) {
            return {};
        }
        var rebuiltObj:Object = {};
        for (var prop:String in monObj) {
            if (prop == "auras") {
                rebuiltObj[prop] = rebuildAuraArray(monObj.auras);
            } else {
                rebuiltObj[prop] = monObj[prop];
            }
        }
        return rebuiltObj;
    }

    public static function HasAnyActiveAura(subject:String, auraNames:String):String {
        var auraList:Array = auraNames.split(',');
        var auras:Object = null;
        try {
            auras = getSubjectAuras(subject);
        } catch (e:Error) {
            return false.toString();
        }

        var auraCount:int = auras.length;
        var auraListCount:int = auraList.length;
        
        for (var i:int = 0; i < auraCount; i++) {
            var aura:Object = auras[i];
            var auraNameLower:String = aura.nam.toLowerCase();
            for (var j:int = 0; j < auraListCount; j++) {
                if (auraNameLower == auraList[j].toLowerCase().trim()) {
                    return true.toString();
                }
            }
        }
        return false.toString();
    }

    public static function GetAurasValue(subject:String, auraName:String):Number {
        var aura:Object = null;
        var auras:Array = null;
        try {
            auras = getSubjectAuras(subject);
        } catch (e:Error) {
            return 442;
        }

        var lowerAuraName:String = auraName.toLowerCase();
        for (var i:int = 0; i < auras.length; i++) {
            aura = auras[i];
            if (aura.nam.toLowerCase() == lowerAuraName) {
                return aura.val;
            }
        }
        return 444;
    }

    public static function GetPlayerAura(playerName:String):String {
        var plrUser:String = playerName.toLowerCase();
        try {
            var userObj:* = instance.game.world.uoTree[plrUser];
            if (!userObj) {
                return '[]';
            }
            return JSON.stringify(rebuildAuraArray(userObj.auras))
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function GetCurrentPlayerAura():String {
        var plrUser:String = instance.game.sfc.myUserName.toLowerCase();
        try {
            var userObj:* = instance.game.world.uoTree[plrUser];
            if (!userObj) {
                return 'Error: Couldn\'t get User Object Tree';
            }
            return JSON.stringify(rebuildAuraArray(userObj.auras));
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function GetMonsterAuraByTarget():String {
        try {
            var monID:int = 0;
            if (instance.game.world.myAvatar.target != null) {
                monID = instance.game.world.myAvatar.target.dataLeaf.MonMapID;
            }
            var monObj:* = instance.game.world.monTree[monID];
            if (!monObj) {
                return 'Error: Couldn\'t get Monster Object Tree';
            }
            return JSON.stringify(rebuildAuraArray(monObj.auras));
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function GetMonsterAuraByName(monsterName:String):String {

        try {
            var monID:int = 0;
            var lowerMonsterName:String = monsterName.toLowerCase();
            for each (var monster:* in instance.game.world.monsters)
            {
                if (monster && monster.objData.strMonName.toLowerCase() == lowerMonsterName) {
                    monID = monster.objData.MonMapID
                }
            }
            var monObj:* = instance.game.world.monTree[monID];
            if (!monObj) {
                return 'Error: Couldn\'t get Monster Object Tree';
            }
            return JSON.stringify(rebuildAuraArray(monObj.auras));
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function GetMonsterAuraByID(monID:int):String {
        try {
            var monObj:* = instance.game.world.monTree[monID];
            if (!monObj) {
                return 'Error: Couldn\'t get Monster Object Tree';
            }
            return JSON.stringify(rebuildAuraArray(monObj.auras));
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function getAvatar(id:int):String {
        return JSON.stringify(instance.game.world.avatars[id].objData);
    }

    public static function clickServer(serverName:String):String {
        var source:* = instance.game.mcLogin.sl.iList;
        for (var i:int = 0; i < source.numChildren; i++) {
            var child:* = source.getChildAt(i);

            if (child.tName.ti.text.toLowerCase().indexOf(serverName.toLowerCase()) > -1) {
                child.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                return true.toString();
            }
        }
        return false.toString();
    }

    public static function isLoggedIn():String {
        return (instance.game != null && instance.game.sfc != null && instance.game.sfc.isConnected).toString();
    }

    public static function isKicked():String {
        return (instance.game.mcLogin != null && instance.game.mcLogin.warning.visible).toString();
    }

    public static function canUseSkill(index:int):String {
        var skill:* = instance.game.world.actions.active[index];
        return (instance.game.world.myAvatar.target != null && instance.game.world.myAvatar.target.dataLeaf.intHP > 0 && ExtractedFuncs.actionTimeCheck(skill) && skill.isOK && !skill.skillLock && !skill.lock).toString();
    }

    public static function walkTo(xPos:int, yPos:int, walkSpeed:int):void {
        walkSpeed = (walkSpeed == 8 ? instance.game.world.WALKSPEED : walkSpeed);
        instance.game.world.myAvatar.pMC.walkTo(xPos, yPos, walkSpeed);
        instance.game.world.moveRequest({
            'mc': instance.game.world.myAvatar.pMC,
            'tx': xPos,
            'ty': yPos,
            'sp': walkSpeed
        });
    }

    public static function untargetSelf():void {
        var target:* = instance.game.world.myAvatar.target;
        if (target && target == instance.game.world.myAvatar) {
            instance.game.world.cancelTarget();
        }
    }

    public static function attackMonsterByID(id:int):String {
        var bestTarget:* = getBestMonsterTargetByID(id);
        return attackTarget(bestTarget);
    }

    public static function attackMonsterByName(name:String):String {
        var bestTarget:* = getBestMonsterTarget(name);
        return attackTarget(bestTarget);
    }

    public static function attackPlayer(name:String):String {
        var player:* = instance.game.world.getAvatarByUserName(name.toLowerCase());
        return attackTarget(player);
    }
    
    private static function sortMonstersByHP(a:*, b:*):Number {
        var aHP:int = (a.dataLeaf && a.dataLeaf.intHP) ? a.dataLeaf.intHP : 0;
        var bHP:int = (b.dataLeaf && b.dataLeaf.intHP) ? b.dataLeaf.intHP : 0;

        var aAlive:Boolean = aHP > 0;
        var bAlive:Boolean = bHP > 0;

        if (aAlive != bAlive) {
            return aAlive ? -1 : 1;
        }

        if (aHP != bHP) {
            return aHP - bHP;
        }

        var aMapID:int = a.objData ? a.objData.MonMapID : 0;
        var bMapID:int = b.objData ? b.objData.MonMapID : 0;
        return aMapID - bMapID;
    }
    public static function getBestMonsterTarget(name:String):* {
        var targetCandidates:Array = [];
        var world:* = instance.game.world;
        var lowerName:String = name.toLowerCase();
        var isWildcard:Boolean = name == '*';

        for each (var monster:* in world.getMonstersByCell(world.strFrame)) {
            if (monster.pMC != null) {
                var monName:String = monster.objData.strMonName.toLowerCase();
                if (isWildcard || monName.indexOf(lowerName) > -1) {
                    targetCandidates.push(monster);
                }
            }
        }

        if (targetCandidates.length == 0)
            return null;

        targetCandidates.sort(sortMonstersByHP);
        return targetCandidates[0];
    }

    public static function getBestMonsterTargetByID(id:int):* {
        var targetCandidates:Array = [];
        var world:* = instance.game.world;

        for each (var monster:* in world.getMonstersByCell(world.strFrame)) {
            if (monster.pMC != null && monster.objData && (monster.objData.MonMapID == id || monster.objData.MonID == id)) {
                targetCandidates.push(monster);
            }
        }

        if (targetCandidates.length == 0)
            return null;

        targetCandidates.sort(sortMonstersByHP);
        return targetCandidates[0];
    }

    public static function availableMonstersInCell():String {
        var retMonsters:Array = [];
        var world:* = instance.game.world;
        
        for each (var monster:* in world.getMonstersByCell(world.strFrame)) {
            if (monster.pMC != null) {
                retMonsters.push(getMonData(monster));
            }
        }
        return JSON.stringify(retMonsters);
    }

    public static function getTargetMonster():String {
        var world:* = instance.game.world;
        var monster:* = world.myAvatar.target
        if (!monster || (monster.dataLeaf && monster.dataLeaf.intHP <= 0)) {
            world.cancelTarget();
            return JSON.stringify({});
        }
        return JSON.stringify(getMonData(monster));
    }

    public static function getMonsters():String {
        var retMonsters:Array = [];
        for each (var monster:* in instance.game.world.monsters) {
            retMonsters.push(getMonData(monster));
        }
        return JSON.stringify(retMonsters);
    }

    public static function getMonData(mon:Object):Object
    {
        var monsterData:Object = {};
        for (var prop:String in mon.objData) {
            monsterData[prop] = mon.objData[prop];
        }
        if (mon.dataLeaf) {
            monsterData.intHP = mon.dataLeaf.intHP;
            monsterData.intHPMax = mon.dataLeaf.intHPMax;
            monsterData.intState = mon.dataLeaf.intState;
        }
        return monsterData;
    }

    public function requestDoomArenaPVPQueue():void {
        instance.game.world.rootClass.sfc.sendXtMessage("zm", "PVPQr", ["doomarena", 0], "str", instance.game.world.rootClass.world.curRoom);
    }

    private static function attackTarget(target:*):String {
        if (target != null && target.pMC != null) {
            instance.game.world.setTarget(target);
            instance.game.world.approachTarget();
            return true.toString();
        }
        return false.toString();
    }

    public static function useSkill(index:int):String {
        var skill:* = instance.game.world.actions.active[index];
        if (skill != null && ExtractedFuncs.actionTimeCheck(skill)) {
            instance.game.world.testAction(skill);
            return true.toString();
        }

        return false.toString();
    }

    public static function magnetize():void {
        var target:* = instance.game.world.myAvatar.target;
        if (target) {
            target.pMC.x = instance.game.world.myAvatar.pMC.x;
            target.pMC.y = instance.game.world.myAvatar.pMC.y;
        }
    }

    public static function infiniteRange():void {
        var active:Array = instance.game.world.actions.active;
        for (var i:int = 0; i < 6; i++) {
            active[i].range = 20000;
        }
    }

    public static function skipCutscenes():void {
        while (instance.game.mcExtSWF.numChildren > 0) {
            instance.game.mcExtSWF.removeChildAt(0);
        }
        instance.game.showInterface();
    }

    public static function killLag(enable:Boolean):void {
        instance.game.world.visible = !enable;
        
        if (instance.customBGLagKiller) {
            instance.customBGLagKiller.visible = enable;
        }
    }

    public static function disableFX(enabled:Boolean):void {
        if (!_fxLastOpt && enabled) {
            _fxStore = {};
        }
        _fxLastOpt = enabled;
        for each (var avatar:* in instance.game.world.avatars) {
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

    public static function hidePlayers(enabled:Boolean):void {
        var world:* = instance.game.world;
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

    public static function buyItemByName(name:String, quantity:int = -1):void {
        var item:* = getShopItem(name);
        if (item != null) {
            if (quantity == -1)
                instance.game.world.sendBuyItemRequest(item);
            else {
                var buyItem:* = {};
                buyItem.iSel = item;
                buyItem.iQty = quantity;
                buyItem.accept = 1;
                instance.game.world.sendBuyItemRequestWithQuantity(buyItem);
            }
        }
    }

    public static function buyItemByID(id:int, shopItemID:int, quantity:int = -1):void {
        var item:* = getShopItemByID(id, shopItemID);
        if (item != null) {
            if (quantity == -1)
                instance.game.world.sendBuyItemRequest(item);
            else {
                var buyItem:* = {};
                buyItem.iSel = item;
                buyItem.iQty = quantity;
                buyItem.accept = 1;
                instance.game.world.sendBuyItemRequestWithQuantity(buyItem);
            }
        }
    }

    public static function getShopItem(name:String):* {
        var lowerName:String = name.toLowerCase();
        for each (var item:* in instance.game.world.shopinfo.items) {
            if (item && item.sName.toLowerCase() == lowerName) {
                return getShopItemByID(item.ID, item.ShopItemID);
            }
        }
        return null;
    }

    public static function getShopItemByID(itemID:int, shopItemID:int):* {
        for each (var item:* in instance.game.world.shopinfo.items) {
            if (item && item.ItemID == itemID && (shopItemID == -1 || item.ShopItemID == shopItemID)) {
                return item;
            }
        }
        return null;
    }

    private static function parseDrop(name:*):* {
        var ret:* = {};
        var lowercaseName:String = name.toLowerCase().trim();
        ret.name = lowercaseName;
        ret.count = 1;
        var result:Object = DROP_PARSE_REGEX.exec(lowercaseName);
        if (result == null) {
            return ret;
        } else {
            ret.name = result[1];
            ret.count = int(result[2]);
            return ret;
        }
    }

    public static function rejectExcept(whitelist:String):void {
        var pickup:Array = whitelist.split(',');
        if (instance.game.litePreference.data.bCustomDrops) {
            var source:* = instance.game.cDropsUI.mcDraggable ? instance.game.cDropsUI.mcDraggable.menu : instance.game.cDropsUI;
            for (var i:int = 0; i < source.numChildren; i++) {
                var child:* = source.getChildAt(i);
                if (child.itemObj) {
                    var itemName:String = child.itemObj.sName.toLowerCase();
                    if (pickup.indexOf(itemName) == -1) {
                        child.btNo.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                    }
                }
            }
        } else {
            var children:int = instance.game.ui.dropStack.numChildren;
            for (i = 0; i < children; i++) {
                child = instance.game.ui.dropStack.getChildAt(i);
                var type:String = getQualifiedClassName(child);
                if (type.indexOf('DFrame2MC') != -1) {
                    var drop:* = parseDrop(child.cnt.strName.text);
                    var name:* = drop.name;
                    if (pickup.indexOf(name) == -1) {
                        child.cnt.nbtn.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                    }
                }
            }
        }
    }

    public static function injectScript(uri:String):void {
        var ploader:Loader = new Loader();
        ploader.contentLoaderInfo.addEventListener(Event.COMPLETE, onScriptLoaded);
        var context:LoaderContext = new LoaderContext();
        context.allowCodeImport = true;
        ploader.load(new URLRequest(uri), context);
    }

    private static function onScriptLoaded(event:Event):void {
        try {
            var obj:* = LoaderInfo(event.target).loader.content;
            obj.run(instance);
        } catch (ex:Error) {
            instance.external.debug('Error while running injection: ' + ex);
        }
    }

    public static function catchPackets():void {
        instance.game.sfc.addEventListener(SFSEvent.onDebugMessage, packetReceived);
    }

    public static function sendClientPacket(packet:String, type:String):void {
        if (_handler == null) {
            var cls:Class = Class(instance.gameDomain.getDefinition('it.gotoandplay.smartfoxserver.handlers.ExtHandler'));
            _handler = new cls(instance.game.sfc);
        }
        switch (type) {
            case 'xml':
                xmlReceived(packet);
                break;
            case 'json':
                jsonReceived(packet);
                break;
            case 'str':
                strReceived(packet);
                break;
            default:
                instance.external.debug('Invalid packet type.');
        }
    }

    public static function xmlReceived(packet:String):void {
        _handler.handleMessage(new XML(packet), 'xml');
    }

    public static function jsonReceived(packet:String):void {
        _handler.handleMessage(JSON.parse(packet)['b'], 'json');
    }

    public static function strReceived(packet:String):void {
        var array:Array = packet.substr(1, packet.length - 2).split('%');
        _handler.handleMessage(array.splice(1, array.length - 1), 'str');
    }

    public static function packetReceived(packet:*):void {
        if (packet.params.message.indexOf('%xt%zm%') > -1) {
            instance.external.call('packet', packet.params.message.split(':', 2)[1].trim());
        }
    }

    public static function disableDeathAd(enable:Boolean):void {
        instance.game.userPreference.data.bDeathAd = !enable;
    }

    public static function UserID():int {
        return instance.game.world.myAvatar.uid;
    }

    public static function Gender():String {
        return '"' + instance.game.world.myAvatar.objData.strGender.toUpperCase() + '"';
    }
}
}
