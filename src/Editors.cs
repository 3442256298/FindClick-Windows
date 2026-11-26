using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using L = FindClick.UiText;

namespace FindClick {
    internal sealed class BindingEditor : UserControl {
        private readonly InputMonitor monitor;
        private readonly Button key = Style.Button("", 240, false);
        private readonly Button clear = Style.Button("×", 37, false);
        private readonly Button empty = Style.Button(L.T("留空", "Unbind"), 73, false);
        private readonly Label status;
        private bool recording;
        private Form form;
        internal Binding Value { get; private set; }
        internal event EventHandler Changed;
        internal BindingEditor(InputMonitor monitor, Binding binding) {
            this.monitor = monitor;
            Value = binding == null ? null : binding.Copy();
            Width = 480;
            Height = 57;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Margin = Padding.Empty };
            key.TextAlign = ContentAlignment.MiddleLeft;
            clear.AccessibleName = L.T("清除并重新录入", "Clear and rebind");
            empty.AccessibleName = L.T("清除绑定", "Unbind");
            var tips = new ToolTip();
            Disposed += delegate { tips.Dispose(); };
            tips.SetToolTip(clear, L.T("清除并等待按下新键", "Clear and record a new binding"));
            tips.SetToolTip(empty, L.T("解绑并保留空白", "Remove the binding"));
            key.Click += delegate { BeginCapture(false); };
            clear.Click += delegate { BeginCapture(true); };
            empty.Click += delegate { Cancel(); SetValue(null); };
            buttons.Controls.AddRange(new Control[] { key, clear, empty });
            status = Style.Label("", 9, false, Style.Muted);
            status.Dock = DockStyle.Bottom;
            status.AutoSize = false;
            status.Height = 19;
            Controls.Add(status);
            Controls.Add(buttons);
            Render();
        }
        private void SetValue(Binding value) {
            Value = value;
            Render();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
        private void BeginCapture(bool reset) {
            if (recording) return;
            form = FindForm();
            if (form == null) return;
            if (reset) SetValue(null);
            try {
                monitor.Capture(() => recording && Native.Foreground() == form.Handle, result => {
                    Cancel();
                    if (result != null) SetValue(result);
                });
                recording = true;
                form.Deactivate += Deactivated;
                Render();
            } catch (MacroException e) { status.Text = e.Message; }
        }
        private void Deactivated(object sender, EventArgs e) { Cancel(); }
        internal void Cancel() {
            if (!recording) return;
            recording = false;
            monitor.CancelCapture();
            if (form != null) form.Deactivate -= Deactivated;
            form = null;
            Render();
        }
        private void Render() {
            key.Text = recording ? L.T("正在录入…", "Recording…") : Value == null ? L.T("点击录入", "Click to record") : Value.ToString();
            key.BackColor = recording ? Style.LightBlue : Color.White;
            status.Text = recording ? L.T("按下并松开键盘或鼠标键  ·  Ctrl + Alt + Esc 取消", "Press and release a key or mouse button. Ctrl + Alt + Esc cancels.")
                : L.T("单击录入，× 清除并重新绑定", "Click to record, or select × to clear and rebind.");
        }
        protected override void Dispose(bool disposing) { if (disposing) Cancel(); base.Dispose(disposing); }
    }
    internal static class UiLayout {
        internal static TableLayoutPanel Rows(Form form, int width, int height, params int[] sizes) {
            form.Font = Style.Font(10, false);
            form.ForeColor = Style.Ink;
            form.BackColor = Style.Background;
            form.AutoScaleDimensions = new SizeF(96, 96);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = new Size(width, height);
            Style.IconFor(form);
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = sizes.Length, Padding = new Padding(22, 15, 22, 15) };
            foreach (int size in sizes) rows.RowStyles.Add(size == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, size));
            form.Controls.Add(rows);
            return rows;
        }
        internal static FlowLayoutPanel Flow(params Control[] controls) {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            flow.Controls.AddRange(controls);
            return flow;
        }
        internal static TextBox Text(string value, int length) { return new TextBox { Text = value, MaxLength = length, Anchor = AnchorStyles.Left | AnchorStyles.Right }; }
        internal static NumericUpDown Number(int value, int min, int max, int width) { return new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(value, max)), Width = width, Anchor = AnchorStyles.Left }; }
        internal static DataGridView Grid(string[] labels, int[] weights) {
            var table = new DataGridView {
                Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, GridColor = Style.Border,
                RowHeadersVisible = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false, Margin = Padding.Empty
            };
            table.RowTemplate.Height = 34;
            table.ColumnHeadersHeight = 33;
            table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            table.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(234, 240, 249), ForeColor = Style.Ink, Padding = new Padding(5, 0, 2, 0) };
            table.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Style.Ink, SelectionForeColor = Style.Ink, SelectionBackColor = Style.LightBlue, Padding = new Padding(5, 0, 2, 0) };
            for (int i = 0; i < labels.Length; i++) table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = labels[i], FillWeight = weights[i], SortMode = DataGridViewColumnSortMode.NotSortable });
            return table;
        }
        internal static int Selected(DataGridView grid) { return grid.SelectedRows.Count == 0 ? -1 : grid.SelectedRows[0].Index; }
        internal static void Select(DataGridView grid, int index) {
            if (index >= 0 && index < grid.Rows.Count) grid.CurrentCell = grid.Rows[index].Cells[0];
            else grid.ClearSelection();
        }
        internal static Control Footer(Form form, Button save) {
            var cancel = Style.Button(L.T("取消", "Cancel"), 85, false);
            cancel.DialogResult = DialogResult.Cancel;
            form.CancelButton = cancel;
            var flow = Flow(save, cancel);
            flow.FlowDirection = FlowDirection.RightToLeft;
            flow.Padding = new Padding(0, 6, 0, 0);
            return flow;
        }
        internal static Label Caption(string text) { return Style.Label(text, 10, true, Style.Ink); }
        internal static CheckBox Check(string label, bool state) { return new CheckBox { Text = label, Checked = state, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 13, 0) }; }
        internal static void Tell(Form form, string message) { MessageBox.Show(form, message, form.Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    internal sealed class RuleEditor : Form {
        internal readonly MacroRule Rule;
        private readonly InputMonitor monitor;
        private readonly TextBox name, title;
        private readonly BindingEditor binding;
        private readonly CheckBox enabled, consume, restore;
        private readonly NumericUpDown repeat;
        private readonly DataGridView steps;
        private readonly Button add, edit, up, down, copy, remove;
        internal RuleEditor(MacroRule rule, InputMonitor monitor) {
            Rule = rule;
            this.monitor = monitor;
            Text = L.T("宏规则", "Macro rule");
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            MinimumSize = new Size(700, 540);
            var rows = UiLayout.Rows(this, 782, 588, 38, 35, 64, 39, 39, 0, 47);
            var header = Style.Label(Text, 18, true, Style.Ink);
            rows.Controls.Add(header, 0, 0);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 63));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, L.English ? 161 : 111));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            name = UiLayout.Text(rule.Name, 80);
            title = UiLayout.Text(rule.Title, 170);
            rows.Controls.Add(fields, 0, 1);
            fields.Controls.Add(UiLayout.Caption(L.T("名称", "Name")), 0, 0);
            fields.Controls.Add(name, 1, 0);
            fields.Controls.Add(UiLayout.Caption(L.T("窗口标题包含", "Window title contains")), 2, 0);
            fields.Controls.Add(title, 3, 0);
            var bindRow = UiLayout.Flow();
            var triggerLabel = UiLayout.Caption(L.T("触发键", "Trigger"));
            triggerLabel.AutoSize = false;
            triggerLabel.Size = new Size(65, 35);
            triggerLabel.TextAlign = ContentAlignment.MiddleLeft;
            binding = new BindingEditor(monitor, rule.Trigger);
            bindRow.Controls.AddRange(new Control[] { triggerLabel, binding });
            rows.Controls.Add(bindRow, 0, 2);
            enabled = UiLayout.Check(L.T("启用", "Enabled"), rule.Enabled);
            consume = UiLayout.Check(L.T("拦截原按键", "Block original input"), rule.Consume);
            restore = UiLayout.Check(L.T("执行后恢复指针", "Restore pointer"), rule.RestorePointer);
            repeat = UiLayout.Number(rule.Repeat, 1, 20, 58);
            var repeatLabel = UiLayout.Caption(L.T("执行轮数", "Repetitions"));
            repeatLabel.Margin = new Padding(0, 7, 5, 0);
            repeat.Margin = new Padding(0, 3, 4, 0);
            rows.Controls.Add(UiLayout.Flow(enabled, consume, restore, repeatLabel, repeat), 0, 3);
            add = Style.Button(L.T("添加步骤", "Add step"), 100, true);
            edit = Style.Button(L.T("编辑", "Edit"), 64, false);
            up = Style.Button(L.T("上移", "Up"), 64, false);
            down = Style.Button(L.T("下移", "Down"), 64, false);
            copy = Style.Button(L.T("复制", "Copy"), 64, false);
            remove = Style.Button(L.T("删除", "Delete"), 64, false);
            rows.Controls.Add(UiLayout.Flow(add, edit, up, down, copy, remove), 0, 4);
            steps = UiLayout.Grid(new[] { L.T("顺序", "Order"), L.T("动作", "Action"), L.T("参数", "Details") }, new[] { 10, 28, 62 });
            rows.Controls.Add(steps, 0, 5);
            var save = Style.Button(L.T("保存规则", "Save rule"), 105, true);
            rows.Controls.Add(UiLayout.Footer(this, save), 0, 6);
            add.Click += delegate { ChangeStep(-1); };
            edit.Click += delegate { ChangeStep(UiLayout.Selected(steps)); };
            copy.Click += delegate { int i = UiLayout.Selected(steps); if (i >= 0 && Rule.Steps.Count < MacroRule.MaxSteps) { Rule.Steps.Insert(i + 1, Rule.Steps[i].Copy()); RenderSteps(i + 1); } };
            remove.Click += delegate { int i = UiLayout.Selected(steps); if (i >= 0) { Rule.Steps.RemoveAt(i); RenderSteps(Math.Min(i, Rule.Steps.Count - 1)); } };
            up.Click += delegate { MoveStep(-1); };
            down.Click += delegate { MoveStep(1); };
            steps.SelectionChanged += delegate { ButtonState(); };
            steps.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ChangeStep(e.RowIndex); };
            save.Click += delegate { Save(); };
            RenderSteps(Rule.Steps.Count > 0 ? 0 : -1);
        }
        private void Save() {
            Rule.Name = name.Text.Trim();
            Rule.Title = title.Text.Trim();
            Rule.Enabled = enabled.Checked;
            Rule.Consume = consume.Checked;
            Rule.RestorePointer = restore.Checked;
            Rule.Repeat = (int)repeat.Value;
            Rule.Trigger = binding.Value;
            string problem = Rule.Problem;
            if (problem != null) { UiLayout.Tell(this, problem); return; }
            if (!Rule.HasActions) { UiLayout.Tell(this, L.T("请添加至少一个宏步骤。", "Add at least one macro step.")); return; }
            DialogResult = DialogResult.OK;
        }
        private void ChangeStep(int index) {
            if (index < 0 && Rule.Steps.Count >= MacroRule.MaxSteps) return;
            using (var dialog = new StepEditor(index < 0 ? new MacroStep() : Rule.Steps[index].Copy(), monitor)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (index < 0) {
                    int selected = UiLayout.Selected(steps);
                    index = selected < 0 ? Rule.Steps.Count : selected + 1;
                    Rule.Steps.Insert(index, dialog.Step);
                } else Rule.Steps[index] = dialog.Step;
                RenderSteps(index);
            }
        }
        private void MoveStep(int by) {
            int index = UiLayout.Selected(steps);
            if (index < 0 || index + by < 0 || index + by >= Rule.Steps.Count) return;
            var step = Rule.Steps[index];
            Rule.Steps.RemoveAt(index);
            Rule.Steps.Insert(index + by, step);
            RenderSteps(index + by);
        }
        private void RenderSteps(int index) {
            steps.Rows.Clear();
            for (int i = 0; i < Rule.Steps.Count; i++) {
                var step = Rule.Steps[i];
                string summary = step.Summary + (Rule.IsDeferredConfirm(step) ? L.T("  ·  合并到最后确认", "  ·  Runs as final confirmation") : "");
                steps.Rows.Add(i + 1, step.KindName, summary);
            }
            UiLayout.Select(steps, index);
            ButtonState();
        }
        private void ButtonState() {
            int index = UiLayout.Selected(steps);
            add.Enabled = Rule.Steps.Count < MacroRule.MaxSteps;
            edit.Enabled = remove.Enabled = index >= 0;
            copy.Enabled = index >= 0 && add.Enabled;
            up.Enabled = index > 0;
            down.Enabled = index >= 0 && index + 1 < Rule.Steps.Count;
        }
    }
    internal sealed class StepEditor : Form {
        internal readonly MacroStep Step;
        private readonly ComboBox type, inputMode;
        private readonly TextBox text;
        private readonly CheckBox list, fixedPoint;
        private readonly BindingEditor binding;
        private readonly NumericUpDown duration, x, y;
        private readonly Panel options;
        private readonly Control findBox, inputBox, waitBox;
        private readonly Control coords;
        private readonly NumericUpDown wait;
        internal StepEditor(MacroStep step, InputMonitor monitor) {
            Step = step;
            Text = L.T("编辑步骤", "Edit step");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            var rows = UiLayout.Rows(this, 674, 360, 41, 0, 47);
            type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 231 };
            type.Items.AddRange(Enum.GetValues(typeof(StepKind)).Cast<StepKind>().Select(k => MacroStep.Caption(k)).ToArray());
            rows.Controls.Add(UiLayout.Flow(UiLayout.Caption(L.T("动作", "Action")), type), 0, 0);
            options = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            rows.Controls.Add(options, 0, 1);
            text = UiLayout.Text(step.Text, 240);
            list = UiLayout.Check(L.T("Labelme 列表模式", "Labelme list mode"), step.PreferList);
            var find = Fields(37, 35, 30);
            find.Controls.Add(UiLayout.Caption(L.T("查找文字", "Search text")), 0, 0);
            find.Controls.Add(text, 1, 0);
            find.Controls.Add(list, 1, 1);
            find.Controls.Add(Style.Label(L.T("找字时可用 | 分隔不同写法。", "Use | to separate alternatives in text searches."), 9, false, Style.Muted), 1, 2);
            findBox = find;
            var input = Fields(64, 36, 36, 37, 38);
            binding = new BindingEditor(monitor, step.Input) { Dock = DockStyle.Fill };
            input.Controls.Add(UiLayout.Caption(L.T("按键", "Binding")), 0, 0);
            input.Controls.Add(binding, 1, 0);
            inputMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Anchor = AnchorStyles.Left };
            inputMode.Items.AddRange(new object[] { L.T("点按 / 定时按住", "Tap / timed hold"), L.T("按下（跨步骤）", "Press (hold across steps)"), L.T("松开", "Release") });
            inputMode.SelectedIndex = (int)step.Mode;
            input.Controls.Add(UiLayout.Caption(L.T("按键方式", "Input mode")), 0, 1);
            input.Controls.Add(inputMode, 1, 1);
            duration = UiLayout.Number(step.Kind == StepKind.Input ? step.Milliseconds : 0, 0, 60000, 89);
            input.Controls.Add(UiLayout.Caption(L.T("按住时间", "Hold time")), 0, 2);
            input.Controls.Add(UiLayout.Flow(duration, Style.Label(L.T("毫秒（0 为点按）", "ms (0 = tap)"), 9, false, Style.Muted)), 1, 2);
            fixedPoint = UiLayout.Check(L.T("使用窗口内坐标（左上角为 0, 0）", "Use window coordinates (top left = 0, 0)"), step.FixedPoint);
            input.Controls.Add(fixedPoint, 1, 3);
            x = UiLayout.Number(step.X, 0, 30000, 75);
            y = UiLayout.Number(step.Y, 0, 30000, 75);
            coords = UiLayout.Flow(UiLayout.Caption("X"), x, UiLayout.Caption("Y"), y);
            input.Controls.Add(coords, 1, 4);
            inputBox = input;
            var waiting = Fields(36);
            wait = UiLayout.Number(step.Kind == StepKind.Wait ? step.Milliseconds : 150, 1, 60000, 90);
            waiting.Controls.Add(UiLayout.Caption(L.T("等待时间", "Wait time")), 0, 0);
            waiting.Controls.Add(UiLayout.Flow(wait, Style.Label(L.T("毫秒", "ms"), 9, false, Style.Muted)), 1, 0);
            waitBox = waiting;
            options.Controls.AddRange(new[] { findBox, inputBox, waitBox });
            var save = Style.Button(L.T("保存步骤", "Save step"), 105, true);
            rows.Controls.Add(UiLayout.Footer(this, save), 0, 2);
            save.Click += delegate { Save(); };
            binding.Changed += delegate { UpdateInput(); };
            fixedPoint.CheckedChanged += delegate { UpdateInput(); };
            inputMode.SelectedIndexChanged += delegate { UpdateInput(); };
            type.SelectedIndexChanged += delegate { UpdateType(); };
            type.SelectedIndex = (int)step.Kind;
        }
        private static TableLayoutPanel Fields(params int[] heights) {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = heights.Length };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var h in heights) table.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            return table;
        }
        private void UpdateType() {
            var kind = (StepKind)type.SelectedIndex;
            if (kind != StepKind.Input) binding.Cancel();
            findBox.Visible = kind == StepKind.FindText || kind == StepKind.FindButton;
            inputBox.Visible = kind == StepKind.Input;
            waitBox.Visible = kind == StepKind.Wait;
            UpdateInput();
        }
        private void UpdateInput() {
            fixedPoint.Enabled = binding.Value != null && binding.Value.Device == Device.Mouse && inputMode.SelectedIndex != (int)InputMode.Up;
            coords.Enabled = fixedPoint.Enabled && fixedPoint.Checked;
            duration.Enabled = inputMode.SelectedIndex == (int)InputMode.Tap && (binding.Value == null || !binding.Value.Wheel);
        }
        private void Save() {
            Step.Kind = (StepKind)type.SelectedIndex;
            Step.Text = text.Text.Trim();
            Step.PreferList = list.Checked;
            Step.Input = binding.Value;
            Step.Mode = (InputMode)inputMode.SelectedIndex;
            Step.Milliseconds = Step.Kind == StepKind.Wait ? (int)wait.Value : duration.Enabled ? (int)duration.Value : 0;
            Step.FixedPoint = fixedPoint.Enabled && fixedPoint.Checked;
            Step.X = (int)x.Value;
            Step.Y = (int)y.Value;
            if (!Step.Valid) {
                string message = Step.Kind != StepKind.Input ? L.T("请输入要查找的文字。", "Enter the text to search for.")
                    : Step.Input == null ? L.T("请录入一个键盘或鼠标动作。", "Record a keyboard or mouse input.")
                    : L.T("单独按下或松开时，请录入一个按键。组合键或滚轮请使用点按。", "Use a single key or button for Press or Release. Use Tap for shortcuts and the wheel.");
                UiLayout.Tell(this, message);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}

