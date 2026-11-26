using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace FindClick {
    internal sealed class FindOptions {
        internal string Text = "", Confirm = "";
        internal bool PreferList;
        internal string[] Targets { get { return Text.Split('|').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray(); } }
    }
    internal sealed class TextHit {
        internal readonly string Text, Source;
        internal readonly RectangleF Rect;
        internal Point Center { get { return Point.Round(new PointF(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2)); } }
        internal TextHit(string text, RectangleF rect, string source) { Text = text; Rect = rect; Source = source; }
    }
    internal sealed class MatchSet {
        internal readonly List<TextHit> Targets = new List<TextHit>();
        internal readonly List<TextHit> Confirms = new List<TextHit>();
    }
    internal sealed class Engine {
        private readonly Ocr ocr;
        internal string Languages { get { return ocr == null ? UiText.T("不可用", "Unavailable") : ocr.Languages.Length == 0 ? UiText.T("未安装", "None installed") : ocr.Languages; } }
        internal Engine() { try { ocr = new Ocr(); } catch { } }

        private static string Key(string text) {
            string normalized = (text ?? "").Normalize(NormalizationForm.FormKC).Trim();
            if (HasCjk(normalized)) normalized = string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
            else normalized = string.Join(" ", normalized.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return normalized;
        }
        private static bool HasCjk(string value) { return value.Any(c => c >= '\u3400' && c <= '\u9fff'); }
        internal static bool Same(string a, string b) { return string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase); }
        private static string ControlName(string value) { return value.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim(); }
        private static bool Near(TextHit a, TextHit b) {
            var overlap = RectangleF.Intersect(a.Rect, b.Rect);
            if (overlap.Width <= 0 || overlap.Height <= 0) return false;
            return overlap.Width * overlap.Height >= .55 * Math.Min(a.Rect.Width * a.Rect.Height, b.Rect.Width * b.Rect.Height);
        }
        private static void Add(List<TextHit> hits, TextHit candidate) {
            if (!hits.Any(h => Near(h, candidate))) hits.Add(candidate);
        }

        private static MatchSet ReadControls(IntPtr window, Rectangle bounds, FindOptions rule, bool confirmOnly) {
            var hits = new MatchSet();
            var standaloneText = new List<TextHit>();
            try {
                var root = AutomationElement.FromHandle(window);
                if (root == null) return hits;
                var request = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
                foreach (var property in new[] { AutomationElement.NameProperty, AutomationElement.ControlTypeProperty,
                    AutomationElement.BoundingRectangleProperty, AutomationElement.IsOffscreenProperty, AutomationElement.IsEnabledProperty }) request.Add(property);
                var types = new[] { ControlType.ListItem, ControlType.DataItem, ControlType.TreeItem, ControlType.Button,
                    ControlType.MenuItem, ControlType.Hyperlink, ControlType.Text, ControlType.Custom, ControlType.RadioButton, ControlType.CheckBox };
                var condition = new OrCondition(types.Select(t => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, t)).ToArray());
                var targets = rule.Targets;
                using (request.Activate()) {
                    var elements = root.FindAll(TreeScope.Descendants, condition);
                    foreach (AutomationElement element in elements) {
                        var info = element.Cached;
                        if (info.IsOffscreen || !info.IsEnabled || string.IsNullOrWhiteSpace(info.Name)) continue;
                        var rect = info.BoundingRectangle;
                        if (rect.IsEmpty || !bounds.Contains(new Point((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2)))) continue;
                        var type = info.ControlType;
                        var displayed = type == ControlType.Button || type == ControlType.MenuItem ? ControlName(info.Name) : info.Name;
                        var hit = new TextHit(displayed, new RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height), "界面控件");
                        if (rule.Confirm.Length > 0 && (type == ControlType.Button || type == ControlType.MenuItem || type == ControlType.Hyperlink)
                            && Same(displayed, rule.Confirm) && Native.CanClick(window, hit.Center)) Add(hits.Confirms, hit);
                        if (confirmOnly || !targets.Any(t => Same(t, displayed)) || !Native.CanClick(window, hit.Center)) continue;
                        bool listItem = type == ControlType.ListItem || type == ControlType.DataItem || type == ControlType.TreeItem;
                        if (rule.PreferList && !listItem) continue;
                        if (type == ControlType.Text || type == ControlType.Custom) Add(standaloneText, hit);
                        else Add(hits.Targets, hit);
                    }
                }
            } catch (System.Runtime.InteropServices.COMException) { } catch (ElementNotAvailableException) { } catch (InvalidOperationException) { }
            if (hits.Targets.Count == 0 && !rule.PreferList) hits.Targets.AddRange(standaloneText);
            return hits;
        }

        internal MatchSet ReadPicture(Bitmap frame, Rectangle bounds, FindOptions rule, bool needTarget, bool needConfirm, CancellationToken cancel) {
            var hits = new MatchSet();
            if (ocr == null || ocr.Languages.Length == 0) return hits;
            if (!rule.PreferList) return ReadRegion(frame, bounds, rule, needTarget, needConfirm, cancel);

            var frames = DialogPicture.FindFrames(frame);
            var list = frames.Where(r => r.Width >= frame.Width * .65 && r.Height >= 70 && r.Top < frame.Height * .65)
                .OrderByDescending(r => r.Height).FirstOrDefault();
            if (list.IsEmpty) return hits;
            if (needTarget) {
                var interior = Rectangle.Inflate(list, -1, -1);
                using (var sub = frame.Clone(interior, PixelFormat.Format32bppArgb)) {
                    var recognized = ReadRegion(sub, Translate(interior, bounds.Location), rule, true, false, cancel);
                    hits.Targets.AddRange(recognized.Targets);
                }
            }
            if (needConfirm) {
                var row = frames.Where(r => r.Width >= 38 && r.Width < list.Width * .48 && r.Height >= 16 && r.Height <= 56 &&
                    r.Bottom <= list.Top && list.Top - r.Bottom <= Math.Max(12, r.Height)).ToList();
                foreach (var button in row) {
                    // Leave room for the symbol to the left of the caption, and crop the outline.
                    var interior = Rectangle.FromLTRB(button.Left + Math.Max(2, button.Width / 4), Math.Max(0, button.Top - 2),
                        button.Right - Math.Max(3, button.Width / 10), button.Bottom);
                    using (var sub = frame.Clone(interior, PixelFormat.Format32bppArgb)) {
                        var recognized = ReadRegion(sub, Translate(interior, bounds.Location), rule, false, true, cancel);
                        if (recognized.Confirms.Count == 1)
                            Add(hits.Confirms, new TextHit(recognized.Confirms[0].Text, Translate(button, bounds.Location), "屏幕识字"));
                    }
                }
            }
            return hits;
        }
        private static Rectangle Translate(Rectangle rectangle, Point offset) {
            rectangle.Offset(offset);
            return rectangle;
        }
        private MatchSet ReadRegion(Bitmap frame, Rectangle bounds, FindOptions rule, bool needTarget, bool needConfirm, CancellationToken cancel) {
            var hits = new MatchSet();
            var targets = rule.Targets;
            for (int pass = 0; pass < 4; pass++) {
                cancel.ThrowIfCancellationRequested();
                if ((!needTarget || hits.Targets.Count > 0) && (!needConfirm || hits.Confirms.Count > 0)) break;
                var lines = ocr.Read(frame, bounds, needTarget && targets.Any(HasCjk) || needConfirm && HasCjk(rule.Confirm),
                    pass == 2, pass == 3 ? 1 : 0, 3, pass == 0 || pass == 3);
                var current = new MatchSet();
                foreach (var line in lines) {
                    var run = new List<TextHit>();
                    foreach (var word in line.Words.OrderBy(w => w.Rect.Left)) {
                        if (run.Count > 0) {
                            var previous = run[run.Count - 1];
                            float gap = word.Rect.Left - previous.Rect.Right;
                            if (gap > Math.Max(word.Rect.Height, previous.Rect.Height) * 1.25f) {
                                MatchRun(current, run, targets, rule.Confirm, needTarget, needConfirm);
                                run.Clear();
                            }
                        }
                        run.Add(word);
                    }
                    MatchRun(current, run, targets, rule.Confirm, needTarget, needConfirm);
                }
                if (hits.Targets.Count == 0) hits.Targets.AddRange(current.Targets);
                if (hits.Confirms.Count == 0) hits.Confirms.AddRange(current.Confirms);
            }
            return hits;
        }
        private static void MatchRun(MatchSet hits, List<TextHit> words, string[] targets, string confirm, bool needTarget, bool needConfirm) {
            if (words.Count == 0) return;
            var text = string.Join(" ", words.Select(w => w.Text));
            var joined = new StringBuilder(words[0].Text);
            for (int i = 1; i < words.Count; i++) {
                var prior = words[i - 1];
                var next = words[i];
                // OCR sometimes splits a printed word in the middle. Preserve visibly larger gaps.
                if (next.Rect.Left - prior.Rect.Right > Math.Max(2, Math.Min(next.Rect.Height, prior.Rect.Height) * .36f)) joined.Append(' ');
                joined.Append(next.Text);
            }
            string compact = joined.ToString();
            bool targetMatch = needTarget && targets.Any(t => Same(t, text) || Same(t, compact));
            bool confirmMatch = needConfirm && (Same(confirm, text) || Same(confirm, compact));
            if (!targetMatch && !confirmMatch) return;
            var rect = words.Select(w => w.Rect).Aggregate(RectangleF.Union);
            string match = (targetMatch && targets.Any(t => Same(t, text)) || confirmMatch && Same(confirm, text)) ? text : compact;
            var hit = new TextHit(Key(match), rect, "屏幕识字");
            if (targetMatch) Add(hits.Targets, hit);
            if (confirmMatch) Add(hits.Confirms, hit);
        }
        private MatchSet Find(IntPtr window, FindOptions rule, bool confirmOnly, CancellationToken cancel) {
            var bounds = Native.CaptureBounds(window);
            if (bounds.Width < 4 || bounds.Height < 4) return new MatchSet();
            var hits = ReadControls(window, bounds, rule, confirmOnly);
            cancel.ThrowIfCancellationRequested();
            if (Native.Foreground() != window) throw new MacroException(UiText.T("前台窗口已切换", "the foreground window changed"));
            bool needTarget = !confirmOnly && hits.Targets.Count == 0;
            bool needConfirm = rule.Confirm.Length != 0 && hits.Confirms.Count == 0;
            if (!needTarget && !needConfirm) return hits;
            using (var frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb)) {
                using (var graphics = Graphics.FromImage(frame)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                if (Native.Foreground() != window) throw new MacroException(UiText.T("前台窗口已切换", "the foreground window changed"));
                var picture = ReadPicture(frame, bounds, rule, needTarget, needConfirm, cancel);
                hits.Targets.AddRange(picture.Targets.Where(h => Native.CanClick(window, h.Center)));
                hits.Confirms.AddRange(picture.Confirms.Where(h => Native.CanClick(window, h.Center)));
            }
            return hits;
        }
        internal List<TextHit> FindStep(IntPtr window, MacroStep step, CancellationToken cancel) {
            bool button = step.Kind == StepKind.FindButton;
            var query = new FindOptions { Text = button ? "" : step.Text, Confirm = button ? step.Text : "", PreferList = step.PreferList };
            cancel.ThrowIfCancellationRequested();
            var matches = Find(window, query, button, cancel);
            cancel.ThrowIfCancellationRequested();
            return button ? matches.Confirms : matches.Targets;
        }
    }
}

