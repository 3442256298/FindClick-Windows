using System;
using System.Drawing;
using System.Threading;
using L = FindClick.UiText;

namespace FindClick {
    internal sealed class MacroException : Exception {
        internal MacroException(string message) : base(message) { }
    }
    internal interface IMacroSession : IDisposable {
        void Check();
        void Wait(int milliseconds);
        void FindAndClick(MacroStep step, bool dryRun);
        void Input(MacroStep step);
    }
    internal static class MacroRunner {
        internal static string Run(MacroRule rule, bool dryRun, IMacroSession session) {
            bool canceled;
            return Run(rule, dryRun, session, out canceled);
        }
        internal static string Run(MacroRule rule, bool dryRun, IMacroSession session, out bool canceled) {
            canceled = false;
            using (session) {
                int cycle = 0, index = 0;
                bool confirming = false;
                try {
                    for (cycle = 0; cycle < (dryRun ? 1 : rule.Repeat); cycle++) {
                        for (index = 0; index < rule.Steps.Count; index++) {
                            session.Check();
                            var step = rule.Steps[index];
                            if (rule.IsDeferredConfirm(step)) continue;
                            if (step.Kind == StepKind.FindText || step.Kind == StepKind.FindButton) session.FindAndClick(step, dryRun);
                            else if (!dryRun && step.Kind == StepKind.Wait) session.Wait(step.Milliseconds);
                            else if (!dryRun) session.Input(step);
                        }
                    }
                    if (rule.HasFinalConfirm) {
                        confirming = true;
                        session.Check();
                        session.FindAndClick(rule.FinalConfirmStep(), dryRun);
                    }
                    if (dryRun) return L.T("试找通过。未发送键鼠输入。", "Test passed. No input was sent.");
                    return rule.Repeat == 1 ? L.F("已完成「{0}」。", "Completed ‘{0}’.", rule.Name)
                        : L.F("已完成「{0}」，共 {1} 轮。", "Completed ‘{0}’ ({1} repetitions).", rule.Name, rule.Repeat);
                } catch (OperationCanceledException) {
                    canceled = true;
                    return L.F("已停止「{0}」。", "Stopped ‘{0}’.", rule.Name);
                }
                catch (MacroException e) {
                    string location = confirming ? L.T("最后确认", "Final confirmation") : L.F("第 {0} 轮 · 第 {1} 步", "Pass {0}, step {1}", cycle + 1, index + 1);
                    return L.F("{0}停止，{1}。", "{0} stopped: {1}.", location, e.Message);
                }
            }
        }
    }
    internal sealed class WindowSession : IMacroSession {
        private readonly Engine engine;
        private readonly IntPtr window;
        private readonly uint process;
        private readonly CancellationToken cancel;
        private readonly bool restore;
        private readonly Native.Pt original;
        private Point? lastMove;
        private readonly Native.PressedInput heldAcrossSteps = new Native.PressedInput();
        internal WindowSession(Engine engine, IntPtr window, bool restorePointer, CancellationToken cancel) {
            this.engine = engine;
            this.window = window;
            this.cancel = cancel;
            process = Native.ProcessOf(window);
            restore = restorePointer && Native.GetCursorPos(out original);
        }
        public void Check() {
            cancel.ThrowIfCancellationRequested();
            if (Native.Foreground() != window || Native.ProcessOf(window) != process) throw new MacroException(L.T("前台窗口已切换", "the foreground window changed"));
        }
        public void Wait(int milliseconds) {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) {
                Check();
                int slice = (int)Math.Max(1, Math.Min(25, milliseconds - clock.ElapsedMilliseconds));
                if (cancel.WaitHandle.WaitOne(slice)) cancel.ThrowIfCancellationRequested();
            }
            Check();
        }
        public void FindAndClick(MacroStep step, bool dryRun) {
            var matches = engine.FindStep(window, step, cancel);
            Check();
            if (matches.Count != 1) throw new MacroException(matches.Count == 0 ? L.F("没找到「{0}」", "no match for ‘{0}’", step.Text)
                : L.F("找到多个「{0}」", "multiple matches for ‘{0}’", step.Text));
            if (dryRun) return;
            Move(matches[0].Center);
            Send(new Binding { Device = Device.Mouse, Code = 1 }, 0);
        }
        public void Input(MacroStep step) {
            if (step.Mode == InputMode.Up) {
                heldAcrossSteps.Release(step.Input);
                return;
            }
            if (step.FixedPoint) {
                Point screen;
                if (!Native.ClientPoint(window, new Point(step.X, step.Y), out screen)) throw new MacroException(L.T("窗口坐标不可点击", "the window coordinates are not clickable"));
                Move(screen);
            }
            if (step.Mode == InputMode.Down) heldAcrossSteps.Press(step.Input, () => CheckInput(step.Input));
            else Send(step.Input, step.Milliseconds);
        }
        private void Send(Binding binding, int duration) {
            using (var held = new Native.PressedInput()) {
                held.Press(binding, () => CheckInput(binding));
                if (duration > 0) Wait(duration);
            }
        }
        private void CheckInput(Binding binding) {
            Check();
            Native.Pt pointer;
            if (binding.Device == Device.Mouse && (!Native.GetCursorPos(out pointer) || !Native.CanClick(window, pointer.Point)))
                throw new MacroException(L.T("鼠标不在目标窗口内", "the pointer is outside the target window"));
        }
        private void Move(Point point) {
            Check();
            if (!Native.Move(window, point)) throw new MacroException(L.T("目标位置被遮挡或不可点击", "the target is covered or cannot be clicked"));
            lastMove = point;
        }
        public void Dispose() {
            heldAcrossSteps.Dispose();
            if (restore && lastMove.HasValue) Native.RestoreIfUnmoved(original, lastMove.Value);
        }
    }
}
