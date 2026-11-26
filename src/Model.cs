using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using L = FindClick.UiText;

namespace FindClick {
    public enum Device { Keyboard, Mouse }
    [Flags] public enum Modifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }
    public enum StepKind { FindText, FindButton, Input, Wait }
    public enum InputMode { Tap, Down, Up }

    public sealed class Binding {
        public Device Device;
        public int Code;
        public Modifiers Modifiers;
        internal bool Valid {
            get {
                if (((int)Modifiers & ~15) != 0) return false;
                if (Device == Device.Mouse) return Code >= 1 && Code <= 7;
                return Device == Device.Keyboard && ((Code >= 8 && Code <= 254 && Code != 0xe7) || Code == 269);
            }
        }
        internal bool Same(Binding b) { return b != null && Device == b.Device && Code == b.Code && Modifiers == b.Modifiers; }
        internal int Id { get { return Code | (Device == Device.Mouse ? 512 : 0); } }
        internal bool Stop { get { return Device == Device.Keyboard && Code == 27 && Modifiers == (Modifiers.Ctrl | Modifiers.Alt); } }
        internal bool Wheel { get { return Device == Device.Mouse && Code >= 6; } }
        internal Binding Copy() { return (Binding)MemberwiseClone(); }
        internal static Modifiers Modifier(int code) {
            switch (code) {
                case 0x10: case 0xa0: case 0xa1: return Modifiers.Shift;
                case 0x11: case 0xa2: case 0xa3: return Modifiers.Ctrl;
                case 0x12: case 0xa4: case 0xa5: return Modifiers.Alt;
                case 0x5b: case 0x5c: return Modifiers.Win;
                default: return Modifiers.None;
            }
        }
        public override string ToString() {
            var names = new List<string>();
            if (Modifiers.HasFlag(Modifiers.Ctrl)) names.Add("Ctrl");
            if (Modifiers.HasFlag(Modifiers.Shift)) names.Add("Shift");
            if (Modifiers.HasFlag(Modifiers.Alt)) names.Add("Alt");
            if (Modifiers.HasFlag(Modifiers.Win)) names.Add("Win");
            names.Add(Device == Device.Mouse ? MouseName(Code) : KeyName(Code));
            return string.Join(" + ", names);
        }
        private static string MouseName(int code) {
            switch (code) {
                case 1: return L.T("鼠标左键", "Left mouse button");
                case 2: return L.T("鼠标右键", "Right mouse button");
                case 3: return L.T("鼠标中键", "Middle mouse button");
                case 4: return L.T("鼠标侧键 1", "Mouse button 4");
                case 5: return L.T("鼠标侧键 2", "Mouse button 5");
                case 6: return L.T("滚轮向上", "Wheel up");
                case 7: return L.T("滚轮向下", "Wheel down");
                default: return "";
            }
        }
        private static string KeyName(int code) {
            switch (code) {
                case 8: return "Backspace";
                case 13: return "Enter";
                case 27: return "Esc";
                case 32: return "Space";
                case 269: return L.T("小键盘 Enter", "Numpad Enter");
                case 0xa0: return L.T("左 Shift", "Left Shift");
                case 0xa1: return L.T("右 Shift", "Right Shift");
                case 0xa2: return L.T("左 Ctrl", "Left Ctrl");
                case 0xa3: return L.T("右 Ctrl", "Right Ctrl");
                case 0xa4: return L.T("左 Alt", "Left Alt");
                case 0xa5: return L.T("右 Alt", "Right Alt");
            }
            if (code >= 48 && code <= 57) return ((char)code).ToString();
            if (code >= 96 && code <= 105) return L.F("小键盘 {0}", "Numpad {0}", code - 96);
            string name = Enum.GetName(typeof(Keys), code);
            return name ?? L.T("键码 ", "Key code ") + code.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
    public sealed class MacroStep {
        public StepKind Kind;
        public string Text = "";
        public bool PreferList;
        public Binding Input;
        public InputMode Mode;
        public int Milliseconds;
        public bool FixedPoint;
        public int X, Y;
        internal MacroStep Copy() { var copy = (MacroStep)MemberwiseClone(); copy.Input = Input == null ? null : Input.Copy(); return copy; }
        internal string KindName { get { return Caption(Kind); } }
        internal static string Caption(StepKind kind) {
            switch (kind) {
                case StepKind.FindText: return L.T("找字并点击", "Find and click text");
                case StepKind.FindButton: return L.T("找按钮并点击", "Find and click button");
                case StepKind.Input: return L.T("键鼠动作", "Keyboard or mouse");
                default: return L.T("等待", "Wait");
            }
        }
        internal string Summary {
            get {
                if (Kind == StepKind.Wait) return L.F("{0} 毫秒", "{0} ms", Milliseconds);
                if (Kind != StepKind.Input) return Text + (PreferList ? "  ·  Labelme" : "");
                string action = Mode == InputMode.Down ? L.T("  ·  按下", "  ·  Press") : Mode == InputMode.Up ? L.T("  ·  松开", "  ·  Release")
                    : Milliseconds > 0 ? L.F("  ·  按住 {0} 毫秒", "  ·  Hold for {0} ms", Milliseconds) : "";
                return Input + action + (FixedPoint ? L.F("  ·  窗口 ({0}, {1})", "  ·  Window ({0}, {1})", X, Y) : "");
            }
        }
        internal bool Valid {
            get {
                if (Kind == StepKind.Wait) return Milliseconds >= 1 && Milliseconds <= 60000;
                if (Kind == StepKind.Input) {
                    if (Input == null || !Input.Valid || Mode < InputMode.Tap || Mode > InputMode.Up) return false;
                    if (FixedPoint && (Mode == InputMode.Up || Input.Device != Device.Mouse || X < 0 || X > 30000 || Y < 0 || Y > 30000)) return false;
                    if (Mode == InputMode.Tap) return Milliseconds >= 0 && Milliseconds <= 60000 && (!Input.Wheel || Milliseconds == 0);
                    return !Input.Wheel && Input.Modifiers == Modifiers.None && Milliseconds == 0;
                }
                return (Kind == StepKind.FindText || Kind == StepKind.FindButton) && Text != null && Text.Length <= 240 && Text.Split('|').Any(s => s.Trim().Length > 0);
            }
        }
    }
    public sealed class MacroRule {
        internal const int MaxSteps = 128, MaxConfirmLength = 240;
        public string Name = "", Title = "";
        // A missing element identifies rules saved before the home-page confirmation field existed.
        public string FinalConfirm;
        public bool FinalConfirmPreferList;
        public bool Enabled = true, Consume = true, RestorePointer = true;
        public int Repeat = 1;
        public Binding Trigger;
        public List<MacroStep> Steps = new List<MacroStep>();
        internal bool Applies(string title) { return Title.Length == 0 || title.IndexOf(Title, StringComparison.OrdinalIgnoreCase) >= 0; }
        internal bool HasFinalConfirm { get { return !string.IsNullOrWhiteSpace(FinalConfirm); } }
        internal bool HasActions { get { return HasFinalConfirm || Steps.Count > 0; } }
        internal bool IsDeferredConfirm(MacroStep step) {
            return HasFinalConfirm && step.Kind == StepKind.FindButton && Engine.Same(step.Text, FinalConfirm);
        }
        internal MacroStep FinalConfirmStep() {
            return new MacroStep {
                Kind = StepKind.FindButton,
                Text = FinalConfirm.Trim(),
                PreferList = FinalConfirmPreferList || Steps.Any(s => s.PreferList &&
                    (s.Kind == StepKind.FindText || IsDeferredConfirm(s)))
            };
        }
        internal void UpgradeFinalConfirm() {
            if (FinalConfirm != null) return;
            FinalConfirm = "";
            // Move a trailing confirmation into the field only when it preserves execution order.
            var last = Steps.LastOrDefault();
            if (Repeat != 1 || last == null || last.Kind != StepKind.FindButton ||
                Steps.Take(Steps.Count - 1).Any(s => s.Kind == StepKind.FindButton && Engine.Same(s.Text, last.Text))) return;
            FinalConfirm = last.Text.Trim();
            FinalConfirmPreferList = last.PreferList;
            Steps.RemoveAt(Steps.Count - 1);
        }
        internal string Problem {
            get {
                if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80) return L.T("请输入规则名称，最多 80 个字符。", "Enter a rule name (up to 80 characters).");
                if (Title == null || Title.Length > 170) return L.T("窗口标题条件过长。", "The window title filter is too long.");
                if (Trigger != null && (!Trigger.Valid || Trigger.Stop)) return L.T("这个按键不能用作触发键。", "This input cannot be used as a trigger.");
                if (Enabled && Trigger == null) return L.T("请录入一个触发键，或把规则暂时停用。", "Record a trigger or disable the rule.");
                if (FinalConfirm != null && FinalConfirm.Length > MaxConfirmLength) return L.T("最后确认文字最多 240 个字符。", "Final confirmation text can be up to 240 characters.");
                if (Steps == null || Steps.Count > MaxSteps || Steps.Any(s => s == null || !s.Valid))
                    return L.T("请配置有效的宏步骤，最多 128 步。", "Configure valid macro steps (up to 128).");
                var held = new HashSet<int>();
                for (int i = 0; i < Steps.Count; i++) {
                    var step = Steps[i];
                    if (step.Kind != StepKind.Input) continue;
                    int key = step.Input.Id;
                    if (step.Mode == InputMode.Down && !held.Add(key)) return L.F("第 {0} 步再次按下了已按住的键。", "Step {0} presses a key that is already held.", i + 1);
                    if (step.Mode == InputMode.Up && !held.Remove(key)) return L.F("第 {0} 步没有对应的按下动作。", "Step {0} has no matching press.", i + 1);
                    if (step.Mode == InputMode.Tap && held.Contains(key)) return L.F("第 {0} 步要点按的键已处于按下状态。", "Step {0} taps a key that is already held.", i + 1);
                }
                if (held.Count != 0) return L.T("请为单独按下的键添加对应的松开步骤。", "Add a matching Release step for each held key.");
                if (Repeat < 1 || Repeat > 20) return L.T("重复次数须为 1 到 20。", "Repetitions must be between 1 and 20.");
                return null;
            }
        }
        internal MacroRule Copy() {
            var copy = (MacroRule)MemberwiseClone();
            copy.Trigger = Trigger == null ? null : Trigger.Copy();
            copy.Steps = Steps.Select(s => s.Copy()).ToList();
            return copy;
        }
        internal static MacroRule Default() {
            return new MacroRule {
                Name = "Labelme · 趴卧", Title = "labelme", Trigger = new Binding { Device = Device.Mouse, Code = 4 },
                FinalConfirm = "OK", FinalConfirmPreferList = true,
                Steps = new List<MacroStep> {
                    new MacroStep { Kind = StepKind.FindText, Text = "趴窝 | 趴卧", PreferList = true },
                    new MacroStep { Kind = StepKind.Wait, Milliseconds = 150 }
                }
            };
        }
    }
    internal sealed class RuleStore {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(List<MacroRule>));
        private readonly string path;
        internal RuleStore(string path) { this.path = path; }
        internal List<MacroRule> Load(string previousPath = null) {
            string source = File.Exists(path) ? path : previousPath;
            if (!File.Exists(source)) return new List<MacroRule> { MacroRule.Default() };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 12000000 };
            using (var reader = XmlReader.Create(source, settings)) {
                var rules = (List<MacroRule>)Serializer.Deserialize(reader);
                if (rules == null || rules.Count > 100 || rules.Any(r => r == null || r.Problem != null)) throw new InvalidDataException(L.T("无效的规则配置。", "Invalid rule configuration."));
                foreach (var rule in rules) rule.UpgradeFinalConfirm();
                return rules;
            }
        }
        internal void Save(List<MacroRule> rules) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staging = path + ".tmp";
            try {
                using (var file = File.Create(staging)) Serializer.Serialize(file, rules);
                if (File.Exists(path)) File.Replace(staging, path, null);
                else File.Move(staging, path);
            } finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }
}

