using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Globalization;

namespace LightWeekTodo
{
    [DataContract] public class Todo
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Text = "";
        [DataMember] public bool Done;
    }
    [DataContract] public class State
    {
        [DataMember] public int Version = 1;
        [DataMember] public Dictionary<string, List<Todo>> Weeks = new Dictionary<string, List<Todo>>();
        [DataMember] public double Left = double.NaN;
        [DataMember] public double Top = double.NaN;
        [DataMember] public double Width = 390;
        [DataMember] public double Height = 540;
        [DataMember] public double Backdrop = 0;
        [DataMember] public bool Pinned;
        [DataMember] public bool PositionLocked;
    }
    public static class Dates
    {
        public static DateTime Monday(DateTime d) { return d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7)); }
        public static string Key(DateTime d) { return Monday(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static List<Todo> Items(State state, DateTime week, bool create)
        {
            string key = Key(week);
            List<Todo> items;
            if (state.Weeks.TryGetValue(key, out items)) return items;
            items = new List<Todo>();
            if (create) state.Weeks[key] = items;
            return items;
        }
    }
    public class Store
    {
        public readonly string Folder;
        public string FilePath { get { return Path.Combine(Folder, "data.json"); } }
        public string Warning;
        public Store(string folder) { Folder = folder; }
        State Read(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                State state = (State)new DataContractJsonSerializer(typeof(State)).ReadObject(stream);
                if (state == null || state.Version != 1 || state.Weeks == null) throw new InvalidDataException("数据格式不正确。");
                foreach (var pair in state.Weeks)
                {
                    DateTime parsed;
                    if (!DateTime.TryParseExact(pair.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) || Dates.Key(parsed) != pair.Key || pair.Value == null)
                        throw new InvalidDataException("周清单格式不正确。");
                    foreach (Todo todo in pair.Value)
                        if (todo == null || todo.Text == null) throw new InvalidDataException("事项格式不正确。");
                }
                state.Width = FiniteClamp(state.Width, 320, 900, 390);
                state.Height = FiniteClamp(state.Height, 340, 1400, 540);
                state.Backdrop = FiniteClamp(state.Backdrop, 0, 0.8, 0);
                if (double.IsInfinity(state.Left)) state.Left = double.NaN;
                if (double.IsInfinity(state.Top)) state.Top = double.NaN;
                return state;
            }
        }
        static double FiniteClamp(double v, double min, double max, double fallback)
        { return double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Max(min, Math.Min(max, v)); }
        public State Load()
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(FilePath))
            {
                if (File.Exists(FilePath + ".bak")) { Warning = "主数据文件缺失，已从上次备份恢复。"; return Read(FilePath + ".bak"); }
                return new State();
            }
            try { return Read(FilePath); }
            catch (Exception ex)
            {
                string preserved = FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
                File.Copy(FilePath, preserved, false);
                if (File.Exists(FilePath + ".bak"))
                {
                    try { State restored = Read(FilePath + ".bak"); File.Delete(FilePath); Warning = "数据读取异常，已恢复上次备份。原文件已保留。"; return restored; }
                    catch { }
                }
                throw new InvalidDataException("清单无法读取，已保留原文件：\n" + preserved + "\n请勿删除数据文件夹。", ex);
            }
        }
        public void Save(State state)
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(State)).WriteObject(stream, state);
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
        }
    }
}
