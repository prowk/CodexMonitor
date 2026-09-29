using System;
using System.Windows;
using System.Windows.Media;
using System.Collections.Generic;
using System.Diagnostics;

namespace CodexMonitor {
    public sealed class GlassSurface : FrameworkElement {
        public const double Padding=12, PanelWidth=360, PanelHeight=316;
        public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register("Progress",typeof(double),typeof(GlassSurface),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
        public double Progress { get{return (double)GetValue(ProgressProperty);} set{SetValue(ProgressProperty,value);} }
        public static readonly DependencyProperty HighlightProperty=DependencyProperty.Register("Highlight",typeof(double),typeof(GlassSurface),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
        public double Highlight { get{return (double)GetValue(HighlightProperty);} set{SetValue(HighlightProperty,value);} }
        readonly Brush material;
        readonly Pen edge,innerEdge;
        readonly Brush[] shadow=new Brush[6];
        public readonly RectangleGeometry ContentClip=new RectangleGeometry();
        public bool MeasureFrames;
        public readonly List<double> FrameTimes=new List<double>();
        public GlassSurface() {
            // 逐像素透明绘制：不启用覆盖整个原生窗口的亚克力着色。
            var fill=new LinearGradientBrush {StartPoint=new Point(.1,0),EndPoint=new Point(.85,1)};
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(236,255,255,255),0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(210,246,249,252),.5));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(228,235,242,248),1));fill.Freeze();material=fill;
            var rim=new LinearGradientBrush(Color.FromArgb(255,255,255,255),Color.FromArgb(130,178,194,208),90);rim.Freeze();edge=new Pen(rim,1);edge.Freeze();
            innerEdge=new Pen(new SolidColorBrush(Color.FromArgb(95,255,255,255)),1);innerEdge.Freeze();
            for(int i=0;i<shadow.Length;i++){var b=new SolidColorBrush(Color.FromArgb((byte)(2+i),35,49,66));b.Freeze();shadow[i]=b;}
            UpdateClip();
        }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
            base.OnPropertyChanged(e);
            if(e.Property==ProgressProperty && ContentClip!=null)UpdateClip();
        }
        void UpdateClip(){ContentClip.Rect=Shape;ContentClip.RadiusX=ContentClip.RadiusY=22+2*Math.Max(0,Math.Min(1,Progress));}
        public Rect Shape {
            get {
                double p=Math.Max(0,Math.Min(1,Progress));
                double w=Anchor.PillWidth+(PanelWidth-Anchor.PillWidth)*p,h=Anchor.PillHeight+(PanelHeight-Anchor.PillHeight)*p;
                return new Rect(Padding+PanelWidth-w,Padding+PanelHeight-h,w,h);
            }
        }
        public bool Contains(Point point) {
            Rect r=Shape;double radius=22+2*Math.Max(0,Math.Min(1,Progress));
            return RoundedContains(r,radius,point);
        }
        public static bool RoundedContains(Rect r,double radius,Point p) {
            if(!r.Contains(p))return false;
            double x=Math.Max(r.Left+radius,Math.Min(r.Right-radius,p.X));
            double y=Math.Max(r.Top+radius,Math.Min(r.Bottom-radius,p.Y));
            double dx=p.X-x,dy=p.Y-y;return dx*dx+dy*dy<=radius*radius;
        }
        protected override HitTestResult HitTestCore(PointHitTestParameters p) {return Contains(p.HitPoint)?new PointHitTestResult(this,p.HitPoint):null;}
        protected override void OnRender(DrawingContext dc) {
            if(MeasureFrames)FrameTimes.Add(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);
            Rect r=Shape;double radius=22+2*Math.Max(0,Math.Min(1,Progress));
            // 阴影只沿圆角轮廓展开，边界以外保持零透明度。
            for(int i=0;i<shadow.Length;i++){double spread=6-i;var s=r;s.Inflate(spread,spread);s.Offset(0,2);dc.DrawRoundedRectangle(shadow[i],null,s,radius+spread,radius+spread);}
            dc.DrawRoundedRectangle(material,edge,r,radius,radius);
            var inside=r;inside.Inflate(-1.5,-1.5);dc.DrawRoundedRectangle(null,innerEdge,inside,radius-1.5,radius-1.5);
            if(Highlight>.001){dc.PushOpacity(.12*Highlight);dc.DrawRoundedRectangle(Brushes.White,null,r,radius,radius);dc.Pop();}
        }
    }
}
