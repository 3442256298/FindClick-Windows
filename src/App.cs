using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using L = FindClick.UiText;

[assembly: AssemblyTitle("找字点击 2")]
[assembly: AssemblyProduct("找字点击 2")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace FindClick {
    internal static class Program {
        [STAThread] private static void Main() {
            bool first;
            using (var single = new Mutex(true, "Local\\FindClick-v2-01b012bb-a59e-41ed-9188-10610558bc2a", out first)) {
                if (!first) {
                    L.English = L.Read(Path.Combine(L.SettingsDirectory, "ui-language.txt"));
                    MessageBox.Show(L.T("找字点击 2 已打开。", "FindClick 2 is already running."), L.T("找字点击 2", "FindClick 2"));
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Application.Run(new MainForm()); }
                finally { Style.ReleaseFonts(); }
            }
        }
    }
    internal sealed class MainForm : Form {
        private readonly uint process = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        private readonly Engine engine = new Engine();
        private readonly InputMonitor monitor;
        private readonly RuleStore store;
        private readonly string oldSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FindClick", "rules.xml");
        private readonly DataGridView grid;
        private readonly DataGridViewTextBoxColumn confirmColumn;
        private readonly Button edit, remove, test, arm, stop, add, import;
        private readonly Label heading, languageLabel, hint, mode, status, footer;
        private readonly ComboBox language;
        private readonly string languagePath;
        private readonly System.Windows.Forms.Timer countdown = new System.Windows.Forms.Timer { Interval = 100 };
        private System.Diagnostics.Stopwatch testClock;
        private List<MacroRule> rules;
        private MacroRule testRule;
        private CancellationTokenSource active;
        private bool armed, editing, closeAfterRun;
        private int activation;
        private int released;
        private int windowChanged;
        private IntPtr runningOn;
        internal MainForm(string alternateConfig = null) {
            monitor = new InputMonitor(this) { Trigger = PrepareTrigger, EmergencyStop = StopListening };
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string previousConfig = alternateConfig == null ? Path.Combine(appData, "FindClick2", "rules.xml") : null;
            string rulesPath = alternateConfig ?? Path.Combine(L.SettingsDirectory, "rules.xml");
            store = new RuleStore(rulesPath);
            languagePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rulesPath)), "ui-language.txt");
            L.English = L.Read(languagePath);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(755, 425);
            var rows = UiLayout.Rows(this, 864, 514, 49, 34, 43, 0, 49, 28);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 1; i < header.ColumnCount; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            heading = Style.Label("", 22, true, Style.Ink);
            languageLabel = UiLayout.Caption("");
            language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100,
                Anchor = AnchorStyles.Left, Margin = new Padding(4, 0, 16, 0) };
            language.Items.AddRange(new object[] { "简体中文", "English" });
            language.SelectedIndex = L.English ? 1 : 0;
            arm = Style.Button("", 132, true);
            arm.Anchor = AnchorStyles.Right;
            arm.Margin = Padding.Empty;
            mode = Style.Label("", 10, true, Style.Muted);
            mode.Margin = new Padding(0, 0, 15, 0);
            header.Controls.Add(heading, 0, 0);
            header.Controls.Add(languageLabel, 1, 0);
            header.Controls.Add(language, 2, 0);
            header.Controls.Add(mode, 3, 0);
            header.Controls.Add(arm, 4, 0);
            rows.Controls.Add(header, 0, 0);
            hint = Style.Label("", 10, false, Style.Muted);
            rows.Controls.Add(hint, 0, 1);
            add = Style.Button("", 100, true);
            edit = Style.Button("", 67, false);
            remove = Style.Button("", 67, false);
            test = Style.Button("", 128, false);
            stop = Style.Button("", 96, false);
            import = Style.Button("", 151, false);
            import.Visible = alternateConfig == null && File.Exists(oldSettings);
            rows.Controls.Add(UiLayout.Flow(add, edit, remove, test, stop, import), 0, 2);
            grid = UiLayout.Grid(ColumnLabels(), new[] { 10, 20, 19, 19, 11, 21 });
            grid.ReadOnly = false;
            foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = true;
            confirmColumn = (DataGridViewTextBoxColumn)grid.Columns[grid.Columns.Count - 1];
            confirmColumn.ReadOnly = false;
            confirmColumn.MaxInputLength = MacroRule.MaxConfirmLength;
            confirmColumn.DefaultCellStyle.BackColor = Color.FromArgb(238, 246, 255);
            rows.Controls.Add(grid, 0, 3);
            var statusBar = new Panel { Dock = DockStyle.Fill, BackColor = Style.LightBlue, Padding = new Padding(10, 0, 8, 0), Margin = new Padding(0, 6, 0, 2) };
            status = Style.Label("", 10, false, Style.Ink);
            status.AutoSize = false;
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            statusBar.Controls.Add(status);
            rows.Controls.Add(statusBar, 0, 4);
            footer = Style.Label("", 9, false, Style.Muted);
            rows.Controls.Add(footer, 0, 5);
            arm.Click += delegate { SetArmed(!armed); };
            add.Click += delegate { EditRule(-1); };
            edit.Click += delegate { EditRule(UiLayout.Selected(grid)); };
            remove.Click += delegate { RemoveRule(); };
            test.Click += delegate { QueueTest(); };
            stop.Click += delegate { CancelWork(); };
            import.Click += delegate { ImportRules(); };
            grid.CellClick += (s, e) => {
                if (e.RowIndex >= 0 && e.ColumnIndex == confirmColumn.Index && !grid.IsCurrentCellInEditMode) grid.BeginEdit(true);
            };
            grid.CellBeginEdit += (s, e) => { e.Cancel = active != null || testRule != null; };
            grid.CellEndEdit += (s, e) => SaveFinalConfirm(e.RowIndex, e.ColumnIndex);
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != confirmColumn.Index) EditRule(e.RowIndex); };
            grid.SelectionChanged += delegate { UpdateButtons(); };
            countdown.Tick += delegate { Tick(); };
            ApplyLanguage();
            status.Text = IdleStatus();
            try { rules = store.Load(previousConfig); }
            catch { rules = new List<MacroRule> { MacroRule.Default() }; status.Text = L.T("无法读取新版规则，已载入示例。", "Could not read the rules. The example was loaded."); }
            Render(0);
            language.SelectedIndexChanged += delegate {
                if (language.SelectedIndex < 0) return;
                if (grid.IsCurrentCellInEditMode) grid.EndEdit();
                int selected = UiLayout.Selected(grid);
                L.English = language.SelectedIndex == 1;
                ApplyLanguage();
                Render(selected);
                status.Text = L.Save(languagePath) ? IdleStatus() : L.T("语言已切换，但无法保存设置。", "Language changed, but the preference could not be saved.");
            };
        }
        private static string[] ColumnLabels() {
            return L.English ? new[] { "Status", "Rule name", "Trigger", "Title filter", "Steps", "Final confirmation" }
                : new[] { "状态", "规则名称", "触发键", "窗口标题包含", "宏步骤", "最后确认" };
        }
        private void ApplyLanguage() {
            Text = heading.Text = L.T("找字点击 2.1", "FindClick 2.1");
            languageLabel.Text = language.AccessibleName = L.T("语言", "Language");
            hint.Text = L.T("点击「最后确认」可直接填写按钮文字。留空时只执行宏步骤。",
                "Edit Final confirmation to set button text. Leave it blank to run only the macro steps.");
            add.Text = L.T("新建规则", "New rule");
            edit.Text = L.T("编辑", "Edit");
            remove.Text = L.T("删除", "Delete");
            test.Text = L.T("3 秒后试找", "Test in 3 sec");
            stop.Text = L.T("停止执行", "Stop macro");
            import.Text = L.T("导入旧版规则", "Import old rules");
            var labels = ColumnLabels();
            for (int i = 0; i < labels.Length; i++) grid.Columns[i].HeaderText = labels[i];
            confirmColumn.ToolTipText = L.T("单击编辑。填写按钮文字后，所有轮次结束时确认一次。",
                "Click to edit. The matching button is clicked once after all repetitions.");
            footer.Text = L.T("Ctrl + Alt + Esc 停止并暂停监听     ·     OCR 语言  ",
                "Ctrl + Alt + Esc to stop and pause     ·     OCR languages: ") + engine.Languages;
            UpdateListeningLabel();
        }
        private void UpdateListeningLabel() {
            mode.Text = armed ? L.T("监听中", "Listening") : L.T("已暂停", "Paused");
            mode.ForeColor = armed ? Color.FromArgb(0, 111, 86) : Style.Muted;
            arm.Text = armed ? L.T("暂停监听", "Pause listening") : L.T("启用监听", "Start listening");
        }
        private string IdleStatus() {
            return armed ? L.T("切换到目标窗口，按下并松开绑定键。", "Switch to the target window and press and release the trigger.")
                : L.T("等待启用。", "Ready.");
        }
        private void SetArmed(bool value) {
            try { monitor.Enable(value); }
            catch (MacroException e) { status.Text = e.Message; return; }
            armed = value;
            activation++;
            if (!value) AbortQueueOrRun();
            UpdateListeningLabel();
            status.Text = value ? IdleStatus() : active == null ? L.T("已暂停监听。", "Listening paused.") : L.T("正在停止执行…", "Stopping…");
            UpdateButtons();
        }
        private void StopListening() { SetArmed(false); }
        private void AbortQueueOrRun() {
            testRule = null;
            testClock = null;
            if (active == null) countdown.Stop();
            if (active != null) active.Cancel();
        }
        private void CancelWork() {
            activation++;
            AbortQueueOrRun();
            if (active == null && !armed) monitor.Enable(false);
            status.Text = active == null ? L.T("已取消试找。", "Test canceled.") : L.T("正在停止执行…", "Stopping…");
            UpdateButtons();
        }
        private void Render(int selected) {
            grid.Rows.Clear();
            foreach (var rule in rules) grid.Rows.Add(rule.Enabled ? L.T("启用", "On") : L.T("停用", "Off"), rule.Name,
                rule.Trigger == null ? "—" : rule.Trigger.ToString(), rule.Title.Length == 0 ? L.T("任意前台窗口", "Any foreground window") : rule.Title,
                L.F("{0} 步", rule.Steps.Count == 1 ? "{0} step" : "{0} steps", rule.Steps.Count) + (rule.Repeat > 1 ? " × " + rule.Repeat : ""), rule.FinalConfirm);
            UiLayout.Select(grid, selected);
            UpdateButtons();
        }
        private void UpdateButtons() {
            bool busy = active != null || testRule != null;
            bool selected = UiLayout.Selected(grid) >= 0;
            test.Enabled = selected && !busy;
            edit.Enabled = remove.Enabled = selected && !busy;
            add.Enabled = !busy && rules.Count < 100;
            import.Enabled = !busy && rules.Count < 100;
            stop.Enabled = busy;
            language.Enabled = !busy;
        }
        private bool Persist(List<MacroRule> next) {
            try { store.Save(next); }
            catch (Exception e) { UiLayout.Tell(this, L.T("无法保存规则。\n", "Could not save the rules.\n") + e.Message); return false; }
            rules = next;
            activation++;
            return true;
        }
        private bool Save(List<MacroRule> next, int selected) {
            if (!Persist(next)) return false;
            Render(selected);
            return true;
        }
        private void SaveFinalConfirm(int row, int column) {
            if (row < 0 || row >= rules.Count || column != confirmColumn.Index) return;
            var cell = grid.Rows[row].Cells[column];
            string text = (cell.Value as string ?? "").Trim();
            if (text == rules[row].FinalConfirm) { cell.Value = text; return; }
            var rule = rules[row].Copy();
            rule.FinalConfirm = text;
            string problem = rule.Problem;
            if (problem != null) {
                cell.Value = rules[row].FinalConfirm;
                status.Text = problem;
                return;
            }
            var next = new List<MacroRule>(rules);
            next[row] = rule;
            bool saved = Persist(next);
            cell.Value = rules[row].FinalConfirm;
            if (!saved) return;
            if (!rule.HasFinalConfirm) status.Text = L.T("最后确认已清除。", "Final confirmation cleared.");
            else if (rule.Steps.Any(rule.IsDeferredConfirm)) status.Text = L.T("已保存。宏中同名的找按钮步骤合并为最后确认。", "Saved. Matching button steps will run once as final confirmation.");
            else status.Text = L.T("最后确认已保存。", "Final confirmation saved.");
        }
        private static bool SameScope(MacroRule a, MacroRule b) {
            return a.Enabled && b.Enabled && a.Trigger != null && a.Trigger.Same(b.Trigger) &&
                string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        }
        private void EditRule(int index) {
            if (active != null || testRule != null || (index < 0 && rules.Count >= 100)) return;
            editing = true;
            activation++;
            try {
                using (var dialog = new RuleEditor(index < 0 ? new MacroRule { FinalConfirm = "" } : rules[index].Copy(), monitor)) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (rules.Where((r, i) => i != index).Any(r => SameScope(r, dialog.Rule))) {
                        UiLayout.Tell(this, L.T("相同的按键和窗口条件已有一条启用的规则。", "An enabled rule already uses this trigger and window filter."));
                        return;
                    }
                    var next = new List<MacroRule>(rules);
                    if (index < 0) { index = next.Count; next.Add(dialog.Rule); }
                    else next[index] = dialog.Rule;
                    if (Save(next, index)) status.Text = L.T("规则已保存。", "Rule saved.");
                }
            } finally { editing = false; }
        }
        private void RemoveRule() {
            int index = UiLayout.Selected(grid);
            if (index < 0 || active != null || testRule != null) return;
            if (MessageBox.Show(this, L.T("删除选中的规则？", "Delete the selected rule?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var next = new List<MacroRule>(rules);
            next.RemoveAt(index);
            if (Save(next, Math.Min(index, next.Count - 1))) status.Text = L.T("规则已删除。", "Rule deleted.");
        }
        private Action PrepareTrigger(Binding binding, out bool consume) {
            consume = false;
            if (!armed || editing || active != null || testRule != null || grid.IsCurrentCellInEditMode) return null;
            if (!rules.Any(rule => rule.Enabled && rule.Trigger != null && rule.Trigger.Same(binding))) return null;
            var window = Native.Foreground();
            if (window == IntPtr.Zero || Native.ProcessOf(window) == process) return null;
            string title = Native.Title(window);
            MacroRule match = null;
            bool tied = false;
            foreach (var rule in rules) {
                if (!rule.Enabled || !rule.HasActions || rule.Trigger == null || !rule.Trigger.Same(binding) || !rule.Applies(title)) continue;
                if (match == null || rule.Title.Length > match.Title.Length) { match = rule; tied = false; }
                else if (rule.Title.Length == match.Title.Length) tied = true;
            }
            if (match == null || tied) return null;
            consume = match.Consume;
            int sequence = activation;
            return delegate {
                if (sequence == activation && armed && !editing && active == null && testRule == null && Native.Foreground() == window)
                    Run(window, match, false);
            };
        }
        private void QueueTest() {
            int index = UiLayout.Selected(grid);
            if (index < 0 || active != null || testRule != null) return;
            try { monitor.Enable(true); }
            catch (MacroException e) { status.Text = e.Message; return; }
            testRule = rules[index].Copy();
            testClock = System.Diagnostics.Stopwatch.StartNew();
            status.Text = L.T("3 秒后试找。请切换到目标窗口。", "Testing in 3 seconds. Switch to the target window.");
            countdown.Start();
            UpdateButtons();
        }
        private void Tick() {
            if (active != null && runningOn != IntPtr.Zero && Native.Foreground() != runningOn && Volatile.Read(ref released) == 0) {
                Interlocked.Exchange(ref windowChanged, 1);
                active.Cancel();
                return;
            }
            if (testRule == null || testClock.ElapsedMilliseconds < 3000) return;
            countdown.Stop();
            var rule = testRule;
            testRule = null;
            testClock = null;
            var window = Native.Foreground();
            if (window == IntPtr.Zero || Native.ProcessOf(window) == process || !rule.Applies(Native.Title(window)))
                status.Text = L.T("试找未运行，请切到匹配窗口标题条件的窗口。", "Test skipped. Switch to a window that matches the title filter.");
            else if (!rule.HasFinalConfirm && !rule.Steps.Any(s => s.Kind == StepKind.FindText || s.Kind == StepKind.FindButton))
                status.Text = L.T("这条规则没有找字或找按钮步骤可试找。", "This rule has no text or button searches to test.");
            else Run(window, rule, true);
            if (active == null && !armed) monitor.Enable(false);
            UpdateButtons();
        }
        private async void Run(IntPtr window, MacroRule rule, bool dry) {
            var cancellation = new CancellationTokenSource();
            active = cancellation;
            runningOn = window;
            Interlocked.Exchange(ref released, 0);
            Interlocked.Exchange(ref windowChanged, 0);
            countdown.Start();
            status.Text = dry ? L.F("正在试找「{0}」…", "Testing ‘{0}’…", rule.Name) : L.F("正在执行「{0}」…", "Running ‘{0}’…", rule.Name);
            UpdateButtons();
            try {
                bool canceled = false;
                string result = await Task.Run(delegate {
                    try { return MacroRunner.Run(rule, dry, new WindowSession(engine, window, rule.RestorePointer, cancellation.Token), out canceled); }
                    finally { Interlocked.Exchange(ref released, 1); }
                });
                status.Text = windowChanged != 0 && canceled ? L.T("前台窗口已切换，宏已停止。", "The foreground window changed. The macro stopped.") : result;
            } catch (Exception e) { status.Text = L.F("执行失败（{0}）。", "Execution failed ({0}).", e.GetBaseException().GetType().Name); }
            finally {
                countdown.Stop();
                runningOn = IntPtr.Zero;
                active = null;
                if (!armed) monitor.Enable(false);
                cancellation.Dispose();
                UpdateButtons();
                if (closeAfterRun) Close();
            }
        }
        private void ImportRules() {
            try {
                using (var reader = XmlReader.Create(oldSettings, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 1000000 })) {
                    var doc = XElement.Load(reader);
                    var next = new List<MacroRule>(rules);
                    int added = 0;
                    foreach (var old in doc.Elements("Rule")) {
                        if (next.Count >= 100) break;
                        int key = (int?)old.Element("Button") ?? 0;
                        if (key < 1 || key > 3) continue;
                        var text = (string)old.Element("Text") ?? "";
                        var confirm = (string)old.Element("Confirm") ?? "";
                        bool list = (bool?)old.Element("PreferList") ?? false;
                        var rule = new MacroRule { Name = text.Substring(0, Math.Min(text.Length, 80)), Title = (string)old.Element("Title") ?? "",
                            Enabled = (bool?)old.Element("Enabled") ?? true, Trigger = new Binding { Device = Device.Mouse, Code = key == 3 ? 3 : key + 3 } };
                        rule.Steps.Add(new MacroStep { Kind = StepKind.FindText, Text = text, PreferList = list });
                        rule.FinalConfirm = confirm.Trim();
                        rule.FinalConfirmPreferList = list;
                        if (rule.HasFinalConfirm) rule.Steps.Add(new MacroStep { Kind = StepKind.Wait, Milliseconds = 150 });
                        if (rule.Problem != null || next.Any(r => r.Trigger != null && r.Trigger.Same(rule.Trigger) &&
                            string.Equals(r.Title, rule.Title, StringComparison.OrdinalIgnoreCase))) continue;
                        next.Add(rule);
                        added++;
                    }
                    if (added == 0) status.Text = L.T("没有可导入的新规则。同按键、同窗口条件的规则会跳过。", "No new rules to import. Duplicate triggers and window filters were skipped.");
                    else if (Save(next, next.Count - added)) status.Text = L.F("已导入 {0} 条规则。", added == 1 ? "Imported {0} rule." : "Imported {0} rules.", added);
                }
            } catch (Exception e) { UiLayout.Tell(this, L.T("无法导入旧版规则。\n", "Could not import old rules.\n") + e.Message); }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (active != null) {
                e.Cancel = true;
                closeAfterRun = true;
                SetArmed(false);
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { countdown.Dispose(); monitor.Dispose(); }
            base.Dispose(disposing);
        }
    }
}


