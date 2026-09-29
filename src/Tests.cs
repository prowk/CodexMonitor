using System;
using System.IO;
using System.Text;
using System.Windows;
namespace CodexMonitor {
    public static class Tests {
        static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
        public static int Run() {
            try {
                var q=QuotaSnapshot.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":74,\"windowDurationMins\":300,\"resetsAt\":1790595019},\"secondary\":{\"usedPercent\":32,\"windowDurationMins\":10080,\"resetsAt\":1791110260}}},\"rateLimits\":{\"primary\":{\"usedPercent\":1,\"windowDurationMins\":60,\"resetsAt\":1790595019}}}");
                Check(q.Primary.Remaining==26 && q.Secondary.Remaining==68,"多桶数据应优先选择主额度桶");
                Check(q.Primary.Label=="5-hour window" && q.Secondary.Label=="Weekly","按服务端窗口长度显示标签");
                q=QuotaSnapshot.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":null}}");Check(q.Primary==null && q.Secondary==null,"缺失数据不能显示为零");
                q=QuotaSnapshot.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":110,\"windowDurationMins\":60,\"resetsAt\":1}}}");Check(q.Primary.Remaining==0,"余量应限制在合法范围");Check(q.Primary.Countdown=="Waiting for reset","跨过重置时间不能自行恢复满额");
                bool rejected=false;try { QuotaSnapshot.Parse("{\"error\":{\"message\":\"auth required\"}}"); }catch(InvalidOperationException){rejected=true;}Check(rejected,"鉴权错误必须可见");
                rejected=false;try { QuotaSnapshot.Parse("{\"rateLimitsByLimitId\":{\"other\":{}},\"rateLimits\":{}}"); }catch(InvalidOperationException){rejected=true;}Check(rejected,"备用桶不能冒充主额度");
                var w=new Native.RECT { Left=100,Top=100,Right=1600,Bottom=1100 };
                var anchor=Anchor.Resolve(w,new Settings(),new Rect(500,900,800,100),w,1.5);
                Check(anchor.X==1300 && anchor.Y==882,"应对齐输入框而非整个窗口，并考虑 DPI 间距");
                var moved=new Native.RECT { Left=-900,Top=300,Right=600,Bottom=1300 };
                anchor=Anchor.Resolve(moved,new Settings(),new Rect(500,900,800,100),w,1.5);
                Check(anchor.X==300 && anchor.Y==1082,"负坐标多屏下应立即补偿窗口位移");
                anchor=Anchor.Resolve(w,new Settings(),new Rect(400,900,500,100),w,1.5);
                Check(anchor.X==900,"侧栏展开后的新输入框应重新居中");
                anchor=Anchor.Resolve(w,new Settings{Manual=true,RightOffset=100,BottomOffset=100},null,w,1.5);
                Check(anchor.X==1450 && anchor.Y==950,"校准位置应按窗口比例与 DPI 跟随");
                Check(!Anchor.IsDrag(3,3,1.5) && Anchor.IsDrag(12,0,1.5),"点击抖动与拖动必须区分");
                Check(!GlassSurface.RoundedContains(new Rect(0,0,236,44),22,new Point(0,0)),"圆角之外不能拦截鼠标");
                Check(GlassSurface.RoundedContains(new Rect(0,0,236,44),22,new Point(118,22)),"胶囊中心应可交互");
                File.WriteAllText("test-results.txt","PASS · 14 项额度解析、边界和定位检查",new UTF8Encoding(false));return 0;
            }catch(Exception e){File.WriteAllText("test-results.txt","FAIL · "+e.Message,new UTF8Encoding(false));return 1;}
        }
    }
}
