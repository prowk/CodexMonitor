using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;

namespace CodexMonitor {
    public static class Native {
        static readonly Dictionary<IntPtr,int[]> placements=new Dictionary<IntPtr,int[]>();
        public static int ResizeCount,RegionCount;
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width { get { return Right-Left; } } public int Height { get { return Bottom-Top; } } }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] struct Accent { public int State, Flags, Color, Animation; }
        [StructLayout(LayoutKind.Sequential)] struct Composition { public int Attribute; public IntPtr Data; public int Size; }
        public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder text, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref Composition value);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1,int y1,int x2,int y2,int w,int h);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr region, bool redraw);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        public static string ImagePath(IntPtr h) {
            uint pid; GetWindowThreadProcessId(h,out pid);
            IntPtr p = OpenProcess(0x1000,false,pid);
            if (p == IntPtr.Zero) return "";
            try { var b = new StringBuilder(2048); int size = b.Capacity; return QueryFullProcessImageName(p,0,b,ref size) ? b.ToString() : ""; }
            finally { CloseHandle(p); }
        }
        public static bool IsCodex(IntPtr h) {
            string path = ImagePath(h).ToLowerInvariant();
            return path.EndsWith("\\codex.exe") || (path.EndsWith("\\chatgpt.exe") && path.Contains("openai.codex"));
        }
        public static IntPtr FindCodex() {
            IntPtr foreground = GetForegroundWindow();
            if (IsCodex(foreground)) return foreground;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h,IntPtr unused) {
                RECT r;
                if (IsWindowVisible(h) && !IsIconic(h) && GetWindowRect(h,out r) && r.Width > 400 && r.Height > 250 && IsCodex(h)) { found=h; return false; }
                return true;
            },IntPtr.Zero);
            return found;
        }
        public static double Scale(IntPtr h) { try { uint d=GetDpiForWindow(h); return d > 0 ? d/96.0 : 1; } catch { return 1; } }
        public static void Prepare(Window w, bool preview) {
            IntPtr h = new WindowInteropHelper(w).Handle;
            // 工具窗口不进入任务栏，不抢走聊天输入框的键盘焦点。
            SetWindowLong(h,-20,GetWindowLong(h,-20) | 0x08000000 | (preview ? 0 : 0x00000080));
            HwndSource.FromHwnd(h).AddHook(delegate(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
                if (msg==0x21) { handled=true; return new IntPtr(3); }
                return IntPtr.Zero;
            });
            // 禁用整窗亚克力和系统非客户区阴影，让 WPF 的逐像素 alpha 决定边界。
            try { int disabled=1;DwmSetWindowAttribute(h,2,ref disabled,4); }catch{}
        }

        public static void Place(Window w, double centerX, double bottom, double scale, int radius) {
            IntPtr h = new WindowInteropHelper(w).Handle;
            int width=(int)Math.Round(w.Width*scale), height=(int)Math.Round(w.Height*scale);
            int x=(int)Math.Round(centerX-width/2.0),y=(int)Math.Round(bottom-height);
            int[] previous;placements.TryGetValue(h,out previous);
            if(previous!=null && previous[0]==x && previous[1]==y && previous[2]==width && previous[3]==height && previous[4]==radius) return;
            if(previous==null || previous[2]!=width || previous[3]!=height)ResizeCount++;
            SetWindowPos(h,new IntPtr(-1),x,y,width,height,0x0010);
            if(radius>0 && (previous==null || previous[2]!=width || previous[3]!=height || previous[4]!=radius)) {
                RegionCount++;
                IntPtr region=CreateRoundRectRgn(0,0,width+1,height+1,(int)(radius*scale),(int)(radius*scale));
                if (SetWindowRgn(h,region,true)==0) DeleteObject(region);
            }
            placements[h]=new int[]{x,y,width,height,radius};
        }
        public static Rect? Composer(IntPtr target) {
            try {
                RECT wr; if (!GetWindowRect(target,out wr)) return null;
                var root=AutomationElement.FromHandle(target);
                AutomationElementCollection edits;
                // 一次缓存所需属性，减少逐项跨进程查询及临时对象。
                var request=new CacheRequest {TreeScope=TreeScope.Element,AutomationElementMode=AutomationElementMode.None};
                request.Add(AutomationElement.IsOffscreenProperty);request.Add(AutomationElement.BoundingRectangleProperty);request.Add(AutomationElement.NameProperty);
                using(request.Activate())edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
                Rect? best=null; double score=Double.MinValue;
                foreach (AutomationElement e in edits) {
                    var cur=e.Cached; var r=cur.BoundingRectangle;
                    if (cur.IsOffscreen || r.IsEmpty || r.Width<180 || r.Height<20 || r.Top<wr.Top+wr.Height*0.4 || r.Bottom>wr.Bottom) continue;
                    string name=(cur.Name ?? "").ToLowerInvariant();
                    if (name.Contains("搜索") || name.Contains("search") || name.Contains("terminal") || name.Contains("终端")) continue;
                    double s=r.Width + r.Top-wr.Top;
                    if(name.Contains("message") || name.Contains("消息") || name.Contains("follow") || name.Contains("询问")) s+=10000;
                    if(s>score) { score=s; best=r; }
                }
                return best;
            } catch { return null; }
        }
    }
}
