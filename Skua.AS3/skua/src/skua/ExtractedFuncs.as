package skua {

public class ExtractedFuncs {
    public function ExtractedFuncs() {
        super();
    }

    public static function actionTimeCheck(skill:*):Boolean {
        var finalCD:* = 0;
        var currentTime:* = new Date().getTime();
        var hasteMultiplier:* = 1 - Math.min(Math.max(Main.instance.getGame().world.myAvatar.dataLeaf.sta.$tha, -1), 0.5);
        if (currentTime - Main.instance.getGame().world.GCDTS < Main.instance.getGame().world.GCD) {
            return false;
        }
        if (skill.OldCD != null) {
            finalCD = Math.round(skill.OldCD * hasteMultiplier);
        } else {
            finalCD = Math.round(skill.cd * hasteMultiplier);
        }
        if (currentTime - skill.ts >= finalCD) {
            delete skill.OldCD;
            return true;
        }
        return false;
    }
}
}
