using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace CodexMonitor {
    public sealed class Settings {
        public int Version=2;
        public bool Manual;
        public double RightOffset=32, BottomOffset=160;
        public static readonly string FilePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json");
        public static Settings Load() {
            try {
                string json=File.ReadAllText(FilePath,Encoding.UTF8);
                // 不迁移旧版的中心点校准，以便首次使用新版时默认右对齐。
                if(!json.Contains("\"Version\""))return new Settings();
                var s=new JavaScriptSerializer().Deserialize<Settings>(json);
                if(s==null || Double.IsNaN(s.RightOffset) || Double.IsInfinity(s.RightOffset) || Double.IsNaN(s.BottomOffset) || Double.IsInfinity(s.BottomOffset))return new Settings();
                return s;
            }catch{return new Settings();}
        }
        public void Save(){File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));}
    }
}
