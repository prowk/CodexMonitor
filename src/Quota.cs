using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexMonitor {
    public sealed class QuotaWindow {
        public double Remaining;
        public int Minutes;
        public DateTime Reset;
        public string Label { get { return Minutes == 10080 ? "Weekly" : Minutes % 60 == 0 ? (Minutes / 60) + "-hour window" : Minutes + "-minute window"; } }
        public string Compact { get { return Minutes == 10080 ? "Week" : Minutes % 60 == 0 ? (Minutes / 60) + "h" : Minutes + "m"; } }
        public string Countdown {
            get {
                var t = Reset - DateTime.Now;
                if (t.TotalSeconds <= 0) return "Waiting for reset";
                if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h until reset";
                return t.TotalHours >= 1 ? (int)t.TotalHours + "h " + t.Minutes + "m until reset" : Math.Ceiling(t.TotalMinutes) + "m until reset";
            }
        }
    }
    public sealed class QuotaSnapshot {
        public QuotaWindow Primary, Secondary;
        public DateTime Updated;
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        public static object Get(Dictionary<string, object> obj, string key) { object v; return obj != null && obj.TryGetValue(key, out v) ? v : null; }
        static QuotaWindow ParseWindow(object value) {
            var m = Map(value);
            if (Get(m, "usedPercent") == null || Get(m, "windowDurationMins") == null || Get(m, "resetsAt") == null) return null;
            double used = Convert.ToDouble(m["usedPercent"]);
            if (Double.IsNaN(used) || Double.IsInfinity(used)) return null;
            return new QuotaWindow { Remaining = Math.Max(0, Math.Min(100, 100 - used)), Minutes = Convert.ToInt32(m["windowDurationMins"]), Reset = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(m["resetsAt"])).ToLocalTime() };
        }
        public static QuotaSnapshot Parse(string json) {
            var root = Map(new JavaScriptSerializer().DeserializeObject(json));
            var error = Map(Get(root, "error"));
            if (error != null) throw new InvalidOperationException("Quota service: " + Convert.ToString(Get(error, "message")));
            var result = Map(Get(root, "result")) ?? root;
            var buckets = Map(Get(result, "rateLimitsByLimitId"));
            // 优先使用主额度桶，不把备用模型额度混入主额度。
            var bucket = buckets != null ? Map(Get(buckets, "codex")) : Map(Get(result, "rateLimits"));
            if (bucket == null) throw new InvalidOperationException("Codex quota is unavailable");
            return new QuotaSnapshot { Primary = ParseWindow(Get(bucket, "primary")), Secondary = ParseWindow(Get(bucket, "secondary")), Updated = DateTime.Now };
        }
    }
    public sealed class QuotaClient : IDisposable {
        readonly object gate = new object();
        Process process;
        bool disposed;
        public static string FindCodex() {
            string custom = Environment.GetEnvironmentVariable("CODEX_MONITOR_CLI");
            if (!String.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(dir)) {
                var files = Directory.GetFiles(dir, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
                if (files.Length > 0) return files[0];
            }
            foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                try { string f = Path.Combine(path.Trim('"'), "codex.exe"); if (File.Exists(f)) return f; } catch { }
            }
            throw new InvalidOperationException("Codex CLI not found. Set CODEX_MONITOR_CLI to codex.exe.");
        }
        static async Task<string> Reply(Process p, int id) {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline) {
                var read = p.StandardOutput.ReadLineAsync();
                if (await Task.WhenAny(read, Task.Delay(Math.Max(1,(int)(deadline-DateTime.UtcNow).TotalMilliseconds))) != read) break;
                string line = await read;
                if (line == null) throw new IOException("Quota service exited");
                try {
                    var obj = QuotaSnapshot.Map(new JavaScriptSerializer().DeserializeObject(line));
                    if (Convert.ToString(QuotaSnapshot.Get(obj,"id")) == id.ToString()) return line;
                } catch (ArgumentException) { }
            }
            throw new TimeoutException("Request timed out. Check your connection.");
        }
        public async Task<QuotaSnapshot> Read() {
            var info = new ProcessStartInfo(FindCodex(), "app-server") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            var p = new Process { StartInfo = info };
            lock (gate) { if (disposed) throw new ObjectDisposedException("QuotaClient"); process = p; p.Start(); }
            p.ErrorDataReceived += delegate { }; p.BeginErrorReadLine();
            try {
                await p.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_monitor\",\"version\":\"1.0.0\"}}}");
                var init = await Reply(p,1);
                if (init.Contains("\"error\"")) throw new IOException("Quota service initialization failed");
                await p.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
                await p.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                return QuotaSnapshot.Parse(await Reply(p,2));
            } finally {
                lock (gate) { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); if (process == p) process = null; }
            }
        }
        public void Dispose() { lock (gate) { disposed = true; try { if (process != null && !process.HasExited) process.Kill(); } catch { } } }
    }
}
