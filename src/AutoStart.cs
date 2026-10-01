using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace CodexMonitor {
    public static class AutoStart {
        public const string TaskName="CodexMonitorWatcher";
        const string LegacyKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string WatcherPath { get {return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"CodexMonitor.Watcher.exe");} }
        static void Release(object value){if(value!=null && Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
        static string Escape(string value){return SecurityElement.Escape(value);}
        internal static string TaskXml(string path,string sid) {
            // 仅使用当前用户的交互会话，不保存密码；接电或电池状态均可启动。
            return "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"+
                "<RegistrationInfo><Description>Start Codex Monitor when this user signs in.</Description></RegistrationInfo>"+
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+Escape(sid)+"</UserId><Delay>PT3S</Delay></LogonTrigger></Triggers>"+
                "<Principals><Principal id=\"User\"><UserId>"+Escape(sid)+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>"+
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"+
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>"+
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"+
                "<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>"+
                "<Actions Context=\"User\"><Exec><Command>"+Escape(path)+"</Command><WorkingDirectory>"+Escape(Path.GetDirectoryName(path))+"</WorkingDirectory></Exec></Actions></Task>";
        }
        public static bool IsEnabled(){return IsEnabled(TaskName,WatcherPath);}
        internal static bool IsEnabled(string name,string path) {
            object service=null,folder=null,task=null,definition=null,actions=null,action=null;
            try {
                service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));((dynamic)service).Connect();
                folder=((dynamic)service).GetFolder("\\");
                try{task=((dynamic)folder).GetTask(name);}catch(FileNotFoundException){return false;}catch(COMException e){if((uint)e.ErrorCode==0x80070002)return false;throw;}
                definition=((dynamic)task).Definition;actions=((dynamic)definition).Actions;action=((dynamic)actions).Item(1);
                return (bool)((dynamic)task).Enabled && String.Equals((string)((dynamic)action).Path,path,StringComparison.OrdinalIgnoreCase);
            }finally{Release(action);Release(actions);Release(definition);Release(task);Release(folder);Release(service);}
        }
        internal static void Register(bool enabled,string name,string path) {
            object service=null,folder=null,task=null;
            try {
                service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));((dynamic)service).Connect();folder=((dynamic)service).GetFolder("\\");
                if(enabled) {
                    if(!File.Exists(path))throw new FileNotFoundException("Watcher executable not found.",path);
                    string sid;using(var identity=WindowsIdentity.GetCurrent())sid=identity.User.Value;
                    // TASK_CREATE_OR_UPDATE=6，TASK_LOGON_INTERACTIVE_TOKEN=3。
                    task=((dynamic)folder).RegisterTask(name,TaskXml(path,sid),6,sid,null,3,null);
                }else {
                    try{((dynamic)folder).DeleteTask(name,0);}catch(FileNotFoundException){}catch(COMException e){if((uint)e.ErrorCode!=0x80070002)throw;}
                }
            }finally{Release(task);Release(folder);Release(service);}
        }
        public static void SetEnabled(bool enabled) {
            Register(enabled,TaskName,WatcherPath);
            if(IsEnabled()!=enabled)throw new IOException("Windows did not save the startup setting.");
            // 新计划任务登记成功后才删除旧启动项，避免迁移失败后丢失设置。
            using(var key=Registry.CurrentUser.OpenSubKey(LegacyKey,true))if(key!=null)key.DeleteValue(TaskName,false);
            if(enabled)StartWatcher();
        }
        public static void StartWatcher() {
            using(var process=Process.Start(new ProcessStartInfo(WatcherPath) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory})){}
        }
        public static int Command(string value) {
            try {
                if(value=="on")SetEnabled(true);else if(value=="off")SetEnabled(false);else if(value!="status")throw new ArgumentException("Use on, off or status.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autostart-results.txt"),"startWithWindows="+IsEnabled()+Environment.NewLine,new System.Text.UTF8Encoding(false));return 0;
            }catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"autostart-results.txt"),"FAIL · "+e.Message,new System.Text.UTF8Encoding(false));return 1;}
        }
        public static int SelfTest() {
            string name=TaskName+".Test."+Process.GetCurrentProcess().Id;
            try {
                Register(true,name,WatcherPath);
                if(!IsEnabled(name,WatcherPath))throw new Exception("Enabled task was not saved.");
                if(IsEnabled(name,WatcherPath+".missing"))throw new Exception("A stale path must not appear enabled.");
                Register(false,name,WatcherPath);
                if(IsEnabled(name,WatcherPath))throw new Exception("Disabled task still exists.");
                Register(false,name,WatcherPath);
                File.WriteAllText("autostart-test-results.txt","PASS · create/read/path validation/disable/repeated disable",new System.Text.UTF8Encoding(false));return 0;
            }catch(Exception e){File.WriteAllText("autostart-test-results.txt","FAIL · "+e,new System.Text.UTF8Encoding(false));return 1;}
            finally{try{Register(false,name,WatcherPath);}catch{}}
        }
    }
}
