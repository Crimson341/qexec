package {
 import flash.display.Sprite;
 import flash.text.TextField;
 import flash.external.ExternalInterface;
 public class FlashProbe extends Sprite {
  public function FlashProbe() {
   var label:TextField = new TextField(); label.width=900; label.height=200;
   label.text='Flash ActionScript 3 is running. ExternalInterface available: '+ExternalInterface.available;
   addChild(label);
   try {
    ExternalInterface.addCallback('loadClient', function():void {label.text += '\nC# loadClient callback succeeded.'; ExternalInterface.call('loaded');});
    ExternalInterface.addCallback('isNull', function(path:String):Boolean {return true;});
    ExternalInterface.call('debug','FLASH_PROBE');
    ExternalInterface.call('requestLoadGame');
   } catch(error:Error) {label.text += '\n'+error.toString();}
  }
 }
}
