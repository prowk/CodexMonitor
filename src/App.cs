using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace CodexMonitor {
    public sealed class MonitorApp : Application {
        static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(35,43,53));
        static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(101,112,124));
        static readonly Brush Healthy=new SolidColorBrush(Color.FromRgb(22,125,83));
        static readonly Brush Low=new SolidColorBrush(Color.FromRgb(196,61,72));
        static readonly CultureInfo English=CultureInfo.GetCultureInfo("en-US");
        readonly QuotaClient client=new QuotaClient();
        readonly Settings settings=Settings.Load();
        readonly bool preview;
        EventWaitHandle lifecycleStop;
        Window window;GlassSurface surface;Canvas layers,content;Grid pill;StackPanel detail;
        TextBlock compactA,compactB,compactLabelA,compactLabelB,status,anchorStatus;
        QuotaRow rowA,rowB;Forms.NotifyIcon tray;QuotaSnapshot snapshot;
        IntPtr target;Rect? composer;Native.RECT observedBounds;
        DispatcherTimer follow,refresh,textTimer;
        bool expanded,reading,locating,paused,quitting,pressed,dragging,wasLeft,wasEscape;
        DateTime nextLocate=DateTime.MinValue,nextFind=DateTime.MinValue,lastComposer=DateTime.MinValue;
        string lastError;double right,bottom,scale=1;
        double placedRight,placedBottom;
        bool animating;
        bool configuringStartup;
        int transitionId;
        readonly TranslateTransform detailMotion=new TranslateTransform();
        Native.POINT pressPoint;double pressRight,pressBottom;
        const double PanelWidth=GlassSurface.PanelWidth,PanelHeight=GlassSurface.PanelHeight;
        const double HostWidth=PanelWidth+24,HostHeight=PanelHeight+24;

        [STAThread] public static int Main(string[] args) {
            if(args.Contains("--self-test"))return Tests.Run();
            if(args.Contains("--startup-test"))return AutoStart.SelfTest();
            int startup=Array.IndexOf(args,"--autostart");if(startup>=0)return AutoStart.Command(startup+1<args.Length?args[startup+1]:"status");
            bool created;
            using(var mutex=new Mutex(true,"Local\\CodexMonitor.Glass.1",out created)) {
                if(!created)return 0;
                var app=new MonitorApp(args.Contains("--preview") || args.Contains("--smoke-test"));
                app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {
                    try{File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),DateTime.Now+" "+e.Exception+Environment.NewLine,Encoding.UTF8);}catch{}
                    Forms.MessageBox.Show("An unexpected error occurred. See error.log.","Codex Monitor");e.Handled=true;app.Shutdown(1);
                };
                return app.Run();
            }
        }
        public MonitorApp(bool mode){preview=mode;ShutdownMode=ShutdownMode.OnExplicitShutdown;}
        protected override async void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);IntPtr initial=Native.GetForegroundWindow();Build();
            if(!preview) {
                lifecycleStop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\CodexMonitor.Stop.1");lifecycleStop.Reset();
                // 手动打开主程序也会启动守护程序，无需分别点击两个可执行文件。
                try{AutoStart.StartWatcher();}catch(Exception failure){File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),DateTime.Now+" Watcher: "+failure+Environment.NewLine,Encoding.UTF8);}
            }
            follow=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(32)};follow.Tick+=delegate{Follow();};follow.Start();
            refresh=new DispatcherTimer {Interval=TimeSpan.FromSeconds(60)};refresh.Tick+=async delegate{await Refresh();};refresh.Start();
            textTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(5)};textTimer.Tick+=delegate{UpdateText();};textTimer.Start();
            Follow();await Refresh();
            if(e.Args.Contains("--smoke-test")) {
                double fixedRight=right,fixedBottom=bottom;
                int resizes=Native.ResizeCount,regions=Native.RegionCount;
                surface.MeasureFrames=true;SetExpanded(true);await Task.Delay(500);surface.MeasureFrames=false;
                Native.RECT r;Native.GetWindowRect(new WindowInteropHelper(window).Handle,out r);
                bool aligned=Math.Abs(r.Right-12*scale-fixedRight)<=1 && Math.Abs(r.Bottom-12*scale-fixedBottom)<=1;
                bool open=expanded && surface.Progress>.999 && detail.Opacity>.99;
                bool panelAlpha=SaveRender("panel-check.png");
                surface.FrameTimes.Clear();surface.MeasureFrames=true;SetExpanded(false);await Task.Delay(500);surface.MeasureFrames=false;
                bool closed=!expanded && surface.Progress<.001 && pill.Opacity>.99;
                bool capsuleAlpha=SaveRender("capsule-check.png");
                // 采样 WPF 绘制回调间隔，不把它等同于显示器的最终呈现帧率。
                var intervals=surface.FrameTimes.Zip(surface.FrameTimes.Skip(1),(x,y)=>y-x).OrderBy(x=>x).ToArray();
                double median=intervals.Length==0?0:intervals[intervals.Length/2];
                double p95=intervals.Length==0?0:intervals[Math.Min(intervals.Length-1,(int)(intervals.Length*.95))];
                SetExpanded(true);await Task.Delay(80);SetExpanded(false);await Task.Delay(400);
                bool reversal=!expanded && surface.Progress<.001 && detail.Visibility==Visibility.Hidden;
                bool stable=Native.ResizeCount==resizes && Native.RegionCount==regions;
                bool focus=Native.GetForegroundWindow()==initial;
                string report="rightAnchor="+aligned+"\nmorphOpen="+open+"\nmorphClose="+closed+"\ninterruptible="+reversal+"\nnoFocusSteal="+focus+"\nliveQuota="+(snapshot!=null)+"\ntransparentPanelCorners="+panelAlpha+"\ntransparentCapsuleCorners="+capsuleAlpha+"\nnoNativeResizingOrRegions="+stable+"\nrenderSamples="+intervals.Length+"\nrenderIntervalMedianMs="+median.ToString("F2",English)+"\nrenderIntervalP95Ms="+p95.ToString("F2",English);
                File.WriteAllText("smoke-results.txt",report,new UTF8Encoding(false));
                Shutdown(aligned && open && closed && focus && snapshot!=null && stable && reversal && panelAlpha && capsuleAlpha?0:1);
            }
        }
        bool SaveRender(string path) {
            window.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)HostWidth,(int)HostHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(layers);
            var pixels=new byte[(int)(HostWidth*HostHeight*4)];bitmap.CopyPixels(pixels,(int)HostWidth*4,0);
            bool clean=true;
            // 验证宿主边缘完全透明，不存在矩形底色。
            for(int x=0;x<(int)HostWidth;x++){if(pixels[x*4+3]!=0 || pixels[((int)HostHeight-1)*(int)HostWidth*4+x*4+3]!=0)clean=false;}
            for(int y=0;y<(int)HostHeight;y++){if(pixels[y*(int)HostWidth*4+3]!=0 || pixels[(y*(int)HostWidth+(int)HostWidth-1)*4+3]!=0)clean=false;}
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(path))png.Save(output);return clean;
        }
        static TextBlock Text(string text,double size,Brush color){return new TextBlock {Text=text,FontSize=size,Foreground=color,VerticalAlignment=VerticalAlignment.Center};}
        static Brush QuotaColor(QuotaWindow quota){return quota==null?Muted:quota.Remaining<30?Low:Healthy;}
        static StackPanel CompactGroup(TextBlock label,TextBlock value) {
            var group=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            label.Margin=new Thickness(0,0,8,0);label.FontWeight=value.FontWeight=FontWeights.SemiBold;
            // 固定数字槽位，避免余量位数变化时标签跟着跳动。
            value.Width=42;value.TextAlignment=TextAlignment.Center;Typography.SetNumeralAlignment(value,FontNumeralAlignment.Tabular);
            group.Children.Add(label);group.Children.Add(value);return group;
        }
        void Build() {
            window=new Window {Title="Codex Monitor",Width=HostWidth,Height=HostHeight,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=preview,ShowActivated=false,Topmost=true,UseLayoutRounding=true,FontFamily=new FontFamily("Segoe UI")};
            TextOptions.SetTextFormattingMode(window,TextFormattingMode.Display);
            layers=new Canvas {Width=HostWidth,Height=HostHeight,Background=null};window.Content=layers;
            surface=new GlassSurface {Width=HostWidth,Height=HostHeight};layers.Children.Add(surface);
            // 内容与玻璃形状共享圆角裁剪，展开期间文字不会漂到玻璃以外。
            content=new Canvas {Width=HostWidth,Height=HostHeight,Clip=surface.ContentClip};layers.Children.Add(content);
            // 分隔线独立居中，两侧等宽，文字长度不会影响分界位置。
            pill=new Grid();pill.ColumnDefinitions.Add(new ColumnDefinition());pill.ColumnDefinitions.Add(new ColumnDefinition());
            compactLabelA=Text("5h",14,Ink);compactLabelB=Text("Week",14,Ink);
            compactA=Text("—",14,Muted);compactB=Text("—",14,Muted);
            var leftGroup=CompactGroup(compactLabelA,compactA);var rightGroup=CompactGroup(compactLabelB,compactB);
            Grid.SetColumn(rightGroup,1);pill.Children.Add(leftGroup);pill.Children.Add(rightGroup);
            var separator=new Border {Width=1,Height=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Color.FromArgb(40,50,65,80)),IsHitTestVisible=false};
            Grid.SetColumnSpan(separator,2);pill.Children.Add(separator);
            var pillContainer=new Grid {Width=Anchor.PillWidth,Height=Anchor.PillHeight};pillContainer.Children.Add(pill);
            Canvas.SetLeft(pillContainer,12+PanelWidth-Anchor.PillWidth);Canvas.SetTop(pillContainer,12+PanelHeight-Anchor.PillHeight);
            content.Children.Add(pillContainer);
            BuildDetail();Canvas.SetLeft(detail,12);Canvas.SetTop(detail,12);content.Children.Add(detail);
            window.Cursor=Cursors.Hand;surface.ContextMenu=Menu();pill.ContextMenu=Menu();detail.ContextMenu=Menu();
            window.PreviewMouseLeftButtonDown+=Down;window.PreviewMouseMove+=Move;window.PreviewMouseLeftButtonUp+=Up;
            window.LostMouseCapture+=delegate{if(pressed)FinishDrag();};
            window.MouseEnter+=delegate{surface.BeginAnimation(GlassSurface.HighlightProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(120)));};
            window.MouseLeave+=delegate{surface.BeginAnimation(GlassSurface.HighlightProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(180)));};
            window.SourceInitialized+=delegate{Native.Prepare(window,preview);};new WindowInteropHelper(window).EnsureHandle();
            tray=new Forms.NotifyIcon {Icon=System.Drawing.SystemIcons.Information,Text="Codex Monitor",Visible=true};
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("Reset to composer right",null,delegate{Dispatcher.Invoke(new Action(ResetAnchor));});
            menu.Items.Add("Refresh quota",null,delegate{Dispatcher.Invoke(new Action(async delegate{await Refresh();}));});
            menu.Items.Add("Pause / resume",null,delegate{Dispatcher.Invoke(new Action(delegate{paused=!paused;Follow();}));});
            var startupItem=new Forms.ToolStripMenuItem("Start with Windows");menu.Items.Add(startupItem);
            menu.Opening+=async delegate {
                startupItem.Enabled=false;
                try{startupItem.Checked=await Task.Run(()=>AutoStart.IsEnabled());startupItem.ToolTipText="Start the watcher when you sign in.";startupItem.Enabled=!configuringStartup;}
                catch{startupItem.ToolTipText="Unable to read the Windows startup setting.";}
            };
            startupItem.Click+=async delegate {await ChangeStartup(!startupItem.Checked);};
            menu.Items.Add("Quit",null,delegate{Dispatcher.Invoke(new Action(delegate{Shutdown();}));});tray.ContextMenuStrip=menu;
            tray.DoubleClick+=delegate{Dispatcher.Invoke(new Action(delegate{if(window.IsVisible)SetExpanded(!expanded);}));};ApplyRestState();
        }
        void BuildDetail() {
            detail=new StackPanel {Width=PanelWidth-48,Margin=new Thickness(22,19,22,15),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Opacity=0,Visibility=Visibility.Hidden,IsHitTestVisible=false};
            detail.RenderTransform=detailMotion;
            var title=Text("Usage",21,Ink);title.FontWeight=FontWeights.SemiBold;detail.Children.Add(title);
            var subtitle=Text("Your remaining Codex allowance",11,Muted);subtitle.Margin=new Thickness(0,4,0,19);detail.Children.Add(subtitle);
            rowA=new QuotaRow();rowB=new QuotaRow();detail.Children.Add(rowA.Root);detail.Children.Add(rowB.Root);
            detail.Children.Add(new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(25,55,70,90)),Margin=new Thickness(0,0,0,10)});
            status=Text("Connecting…",10,Muted);detail.Children.Add(status);
            anchorStatus=Text("Right aligned · Drag to move",10,Muted);anchorStatus.Margin=new Thickness(0,5,0,0);detail.Children.Add(anchorStatus);
        }
        ContextMenu Menu() {
            var m=new ContextMenu();Action<string,Action> add=delegate(string caption,Action action){var item=new MenuItem {Header=caption};item.Click+=delegate{action();};m.Items.Add(item);};
            add("Reset to composer right",ResetAnchor);add("Refresh quota",async delegate{await Refresh();});
            var startup=new MenuItem {Header="Start with Windows",IsCheckable=true,IsEnabled=false};m.Items.Add(startup);
            bool enabled=false;
            m.Opened+=async delegate {
                startup.IsEnabled=false;
                try{enabled=await Task.Run(()=>AutoStart.IsEnabled());startup.IsChecked=enabled;startup.ToolTip="Start the watcher when you sign in.";startup.IsEnabled=!configuringStartup;}
                catch{startup.ToolTip="Unable to read the Windows startup setting.";}
            };
            startup.Click+=async delegate {startup.IsEnabled=false;await ChangeStartup(!enabled);};
            add("Quit",delegate{Shutdown();});return m;
        }
        async Task ChangeStartup(bool enabled) {
            if(configuringStartup)return;configuringStartup=true;
            try{await Task.Run(()=>AutoStart.SetEnabled(enabled));tray.ShowBalloonTip(2500,"Codex Monitor",enabled?"Start with Windows enabled.":"Start with Windows disabled.",Forms.ToolTipIcon.Info);}
            catch(Exception e){File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),DateTime.Now+" Startup setting: "+e+Environment.NewLine,Encoding.UTF8);Forms.MessageBox.Show("Could not update the Windows startup setting. See error.log for details.","Codex Monitor");}
            finally{configuringStartup=false;}
        }
        async Task Refresh() {
            if(reading || quitting)return;reading=true;UpdateText();
            try{snapshot=await client.Read();lastError=null;}catch(Exception e){lastError=e.Message;}
            finally{reading=false;if(!quitting)UpdateText();}
        }
        void UpdateText() {
            var a=snapshot==null?null:snapshot.Primary;var b=snapshot==null?null:snapshot.Secondary;
            bool stale=snapshot!=null && (lastError!=null || (DateTime.Now-snapshot.Updated).TotalMinutes>2 || a!=null && a.Reset<=DateTime.Now || b!=null && b.Reset<=DateTime.Now);
            compactLabelA.Text=a==null?"5h":a.Compact;compactLabelB.Text=b==null?"Week":b.Compact;
            compactA.Text=a==null?"—":a.Remaining.ToString("0",English)+"%";
            compactB.Text=b==null?"—":b.Remaining.ToString("0",English)+"%";
            compactA.Foreground=QuotaColor(a);compactB.Foreground=QuotaColor(b);rowA.Update(a,"Session");rowB.Update(b,"Weekly");
            status.Text=reading?"Refreshing…":lastError!=null?(snapshot==null?"Connection failed · Right-click to retry":"Out of date · Updated "+snapshot.Updated.ToString("HH:mm",English)):stale?"Waiting for server reset":snapshot==null?"Waiting for quota":"Updated "+snapshot.Updated.ToString("HH:mm",English)+" · Refreshes every 60s";
            // 服务端或系统的错误可能含中文，界面只展示英文摘要。
            status.ToolTip=lastError==null?"Reset times use your local time zone.":"Unable to refresh. Check your connection and Codex login.";
            anchorStatus.Text=settings.Manual?"Custom position · Drag to move":composer.HasValue || preview?"Right aligned · Drag to move":"Locating composer · Drag to position";
            string tip="Codex · "+compactLabelA.Text+" "+compactA.Text+"  "+compactLabelB.Text+" "+compactB.Text;tray.Text=tip.Substring(0,Math.Min(63,tip.Length));
        }
        void Follow() {
            if(lifecycleStop!=null && lifecycleStop.WaitOne(0)){Shutdown();return;}
            if(quitting)return;PollDismiss();if(pressed)Move(null,null);
            // 隐藏时降低唤醒频率，显示时保持拖动跟随速度；动画仍由 WPF 时钟驱动。
            var interval=TimeSpan.FromMilliseconds(window.IsVisible?32:250);if(follow.Interval!=interval)follow.Interval=interval;
            if(!pressed) {
                if(preview) {
                    var area=Forms.Screen.PrimaryScreen.WorkingArea;scale=Native.Scale(new WindowInteropHelper(window).Handle);
                    if(!settings.Manual || right==0){right=area.Left+area.Width*.65;bottom=area.Bottom-120;}
                }else {
                    IntPtr fg=Native.GetForegroundWindow();
                    if(DateTime.UtcNow>=nextFind) {
                        if(Native.IsCodex(fg)){if(target!=fg){target=fg;composer=null;nextLocate=DateTime.MinValue;}}
                        else if(target==IntPtr.Zero || !Native.IsWindowVisible(target))target=Native.FindCodex();
                        nextFind=DateTime.UtcNow.AddMilliseconds(500);
                    }
                    uint pid;Native.GetWindowThreadProcessId(fg,out pid);bool own=pid==(uint)Process.GetCurrentProcess().Id;Native.RECT r;
                    if(paused || target==IntPtr.Zero || Native.IsIconic(target) || !Native.GetWindowRect(target,out r) || fg!=target && !(own && window.IsVisible)){Hide();return;}
                    scale=Native.Scale(target);Point p=Anchor.Resolve(r,settings,composer,observedBounds,scale);right=p.X;bottom=p.Y;Locate(r);
                }
            }
            if(paused){Hide();return;}Place();if(!window.IsVisible)window.Show();
        }
        void Locate(Native.RECT r) {
            if(animating || settings.Manual || locating || DateTime.UtcNow<nextLocate)return;
            locating=true;nextLocate=DateTime.UtcNow.AddMilliseconds(composer.HasValue?1500:600);IntPtr captured=target;
            Task.Run(delegate{return Native.Composer(captured);}).ContinueWith(t=>{
                if(quitting)return;
                Dispatcher.BeginInvoke(new Action(delegate{
                    locating=false;if(quitting || target!=captured)return;var found=t.Status==TaskStatus.RanToCompletion?t.Result:null;
                    if(found.HasValue){composer=found;observedBounds=r;lastComposer=DateTime.UtcNow;}
                    else if((DateTime.UtcNow-lastComposer).TotalSeconds>4)composer=null;UpdateText();
                }));
            });
        }
        void Hide(){if(!window.IsVisible)return;window.Hide();expanded=false;transitionId++;animating=false;ApplyRestState();}
        void Place() {
            var area=Forms.Screen.FromPoint(new System.Drawing.Point((int)right-1,(int)bottom-1)).WorkingArea;
            // 展开前预留面板空间；动画期间宿主尺寸和右下锚点不变。
            double w=expanded?PanelWidth:Anchor.PillWidth,h=expanded?PanelHeight:Anchor.PillHeight;
            if(!animating){placedRight=Math.Max(area.Left+w*scale+12,Math.Min(area.Right-12,right));placedBottom=Math.Max(area.Top+h*scale+12,Math.Min(area.Bottom-12,bottom));}
            Native.Place(window,placedRight+12*scale-HostWidth*scale/2,placedBottom+12*scale,scale,0);
        }
        static DoubleAnimation Tween(double from,double to,int milliseconds,int delay) {
            return new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(milliseconds)) {BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}};
        }
        void SetExpanded(bool value) {
            if(expanded==value)return;expanded=value;int id=++transitionId;
            window.Cursor=value?Cursors.Arrow:Cursors.Hand;
            animating=false;Place();
            if(!SystemParameters.ClientAreaAnimation){ApplyRestState();return;}
            animating=true;
            // 使用 WPF 动画时钟，仅重绘形状与透明度，不调整 HWND 或内容布局。
            pill.Visibility=Visibility.Visible;detail.Visibility=Visibility.Visible;
            pill.IsHitTestVisible=false;detail.IsHitTestVisible=value;
            var shape=Tween(surface.Progress,value?1:0,value?320:260,0);
            shape.Completed+=delegate {if(id!=transitionId || quitting)return;animating=false;ApplyRestState();Place();};
            surface.BeginAnimation(GlassSurface.ProgressProperty,shape);
            pill.BeginAnimation(UIElement.OpacityProperty,Tween(pill.Opacity,value?0:1,value?80:120,value?0:130));
            detail.BeginAnimation(UIElement.OpacityProperty,Tween(detail.Opacity,value?1:0,value?170:90,value?90:0));
            detailMotion.BeginAnimation(TranslateTransform.YProperty,Tween(value?8:detailMotion.Y,value?0:5,value?240:180,0));
        }
        void ApplyRestState() {
            surface.BeginAnimation(GlassSurface.ProgressProperty,null);surface.Progress=expanded?1:0;
            pill.BeginAnimation(UIElement.OpacityProperty,null);detail.BeginAnimation(UIElement.OpacityProperty,null);
            detailMotion.BeginAnimation(TranslateTransform.YProperty,null);detailMotion.Y=0;
            pill.Opacity=expanded?0:1;detail.Opacity=expanded?1:0;
            pill.Visibility=expanded?Visibility.Hidden:Visibility.Visible;detail.Visibility=expanded?Visibility.Visible:Visibility.Hidden;
            pill.IsHitTestVisible=!expanded;detail.IsHitTestVisible=expanded;
        }
        bool InSurface(Native.POINT p) {
            Native.RECT r;Native.GetWindowRect(new WindowInteropHelper(window).Handle,out r);
            return surface.Contains(new Point((p.X-r.Left)/scale,(p.Y-r.Top)/scale));
        }
        void PollDismiss() {
            bool left=Native.GetAsyncKeyState(1)<0,escape=Native.GetAsyncKeyState(27)<0;
            if(expanded && window.IsVisible && !pressed && !surface.ContextMenu.IsOpen && !pill.ContextMenu.IsOpen && !detail.ContextMenu.IsOpen) {
                Native.POINT p;Native.RECT r;Native.GetCursorPos(out p);Native.GetWindowRect(new WindowInteropHelper(window).Handle,out r);
                if(left && !wasLeft && !InSurface(p) || escape && !wasEscape)SetExpanded(false);
            }
            wasLeft=left;wasEscape=escape;
        }
        void Down(object sender,MouseButtonEventArgs e) {
            Native.GetCursorPos(out pressPoint);if(!InSurface(pressPoint))return;pressRight=placedRight;pressBottom=placedBottom;pressed=true;dragging=false;window.CaptureMouse();e.Handled=true;
        }
        void Move(object sender,MouseEventArgs e) {
            if(!pressed)return;Native.POINT p;Native.GetCursorPos(out p);
            if(!dragging && Anchor.IsDrag(p.X-pressPoint.X,p.Y-pressPoint.Y,scale))dragging=true;
            if(dragging){right=pressRight+p.X-pressPoint.X;bottom=pressBottom+p.Y-pressPoint.Y;window.Cursor=Cursors.SizeAll;Place();}
        }
        void Up(object sender,MouseButtonEventArgs e) {
            Move(null,null);bool click=pressed && !dragging;FinishDrag();window.ReleaseMouseCapture();if(click && !expanded)SetExpanded(true);e.Handled=true;
        }
        void FinishDrag() {
            bool save=pressed && dragging;pressed=false;dragging=false;window.Cursor=expanded?Cursors.Arrow:Cursors.Hand;
            if(save) {
                // 记录实际可见位置，避免屏幕边缘裁剪产生隐藏偏移。
                Native.RECT visible;Native.GetWindowRect(new WindowInteropHelper(window).Handle,out visible);right=visible.Right-12*scale;bottom=visible.Bottom-12*scale;
                Native.RECT r;if(target!=IntPtr.Zero && Native.GetWindowRect(target,out r)) {
                    settings.Manual=true;settings.RightOffset=(r.Right-right)/scale;settings.BottomOffset=(r.Bottom-bottom)/scale;SaveSettings();
                }else if(preview)settings.Manual=true;UpdateText();
            }
        }
        void SaveSettings(){try{settings.Save();}catch{tray.ShowBalloonTip(2500,"Codex Monitor","Position applied, but could not be saved.",Forms.ToolTipIcon.Warning);}}
        void ResetAnchor(){settings.Manual=false;SaveSettings();nextLocate=DateTime.MinValue;UpdateText();Follow();}
        protected override void OnExit(ExitEventArgs e) {
            quitting=true;if(follow!=null)follow.Stop();if(refresh!=null)refresh.Stop();if(textTimer!=null)textTimer.Stop();client.Dispose();if(lifecycleStop!=null)lifecycleStop.Dispose();if(tray!=null)tray.Dispose();base.OnExit(e);
        }
        sealed class QuotaRow {
            public readonly StackPanel Root=new StackPanel {Margin=new Thickness(0,0,0,17)};
            readonly TextBlock label,value,time;readonly Border fill;
            public QuotaRow() {
                var head=new DockPanel();value=Text("—",20,Ink);value.FontWeight=FontWeights.SemiBold;DockPanel.SetDock(value,Dock.Right);head.Children.Add(value);label=Text("Session",12,Ink);head.Children.Add(label);Root.Children.Add(head);
                var track=new Border {Height=4,CornerRadius=new CornerRadius(2),Background=new SolidColorBrush(Color.FromArgb(24,45,65,88)),Margin=new Thickness(0,7,0,7)};
                fill=new Border {Width=0,HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(2),Background=Muted};track.Child=fill;Root.Children.Add(track);
                time=Text("Waiting for data",10,Muted);Root.Children.Add(time);
            }
            public void Update(QuotaWindow q,string fallback) {
                label.Text=q==null?fallback:q.Label;value.Text=q==null?"—":q.Remaining.ToString("0",English)+"%";
                value.Foreground=QuotaColor(q);fill.Background=QuotaColor(q);
                double width=q==null?0:q.Remaining/100*(PanelWidth-48);
                if(Math.Abs(fill.Width-width)>.1){double from=fill.ActualWidth;fill.Width=width;fill.BeginAnimation(FrameworkElement.WidthProperty,new DoubleAnimation(from,width,TimeSpan.FromMilliseconds(320)));}
                time.Text=q==null?"No quota data":q.Countdown+" · "+q.Reset.ToString("MMM d, HH:mm",English);
            }
        }
    }
}
