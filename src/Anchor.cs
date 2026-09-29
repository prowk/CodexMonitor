using System;
using System.Windows;
namespace CodexMonitor {
    public static class Anchor {
        public const double PillWidth=236, PillHeight=44;
        public static Point Resolve(Native.RECT window, Settings settings, Rect? composer, Native.RECT observed, double scale) {
            double right=window.Right-32*scale, bottom=window.Bottom-160*scale;
            if(composer.HasValue) {
                Rect c=composer.Value;
                // 输入框位置使用物理坐标，移动时立即补偿窗口位移。
                right=c.Right+window.Left-observed.Left;
                bottom=c.Top-12*scale+window.Top-observed.Top;
            }
            if(settings.Manual) { right=window.Right-settings.RightOffset*scale;bottom=window.Bottom-settings.BottomOffset*scale; }
            return new Point(right,bottom);
        }
        public static bool IsDrag(double dx,double dy,double scale) { return dx*dx+dy*dy>=36*scale*scale; }
    }
}
