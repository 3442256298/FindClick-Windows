using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FindClick {
    internal sealed class InputMonitor : IDisposable {
        internal delegate Action TriggerHandler(Binding binding, out bool consume);
        private readonly Control owner;
        private readonly Native.HookProc keyProc, mouseProc;
        private IntPtr keyboard, mouse;
        private readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private readonly HashSet<int> skipModifiers = new HashSet<int>();
        private readonly HashSet<int> modifiersDown = new HashSet<int>();
        private Action<Binding> capture;
        private Func<bool> captureActive;
        private bool enabled, disposed;
        internal TriggerHandler Trigger;
        internal Action EmergencyStop;
        private sealed class Pending {
            internal readonly Action Fire;
            internal readonly bool Consume;
            internal Pending(Action fire, bool consume) { Fire = fire; Consume = consume; }
        }
        internal InputMonitor(Control owner) {
            this.owner = owner;
            keyProc = Keyboard;
            mouseProc = Mouse;
        }
        private void Install() {
            if (keyboard != IntPtr.Zero && mouse != IntPtr.Zero) return;
            var module = Native.GetModuleHandle(null);
            keyboard = Native.SetWindowsHookEx(Native.KeyboardHook, keyProc, module, 0);
            if (keyboard != IntPtr.Zero) mouse = Native.SetWindowsHookEx(Native.MouseHook, mouseProc, module, 0);
            if (keyboard != IntPtr.Zero && mouse != IntPtr.Zero) return;
            int error = Marshal.GetLastWin32Error();
            Uninstall();
            throw new MacroException(UiText.F("无法启动键鼠监听（Windows 错误 {0}）", "Could not start input monitoring (Windows error {0}).", error));
        }
        internal void Enable(bool value) {
            if (value) Install();
            enabled = value;
            TrimHooks();
        }
        internal void Capture(Func<bool> active, Action<Binding> completed) {
            Install();
            capture = completed;
            captureActive = active;
        }
        internal void CancelCapture() {
            capture = null;
            captureActive = null;
            TrimHooks();
        }
        private void TrimHooks() {
            if (!enabled && capture == null && pending.Count == 0) Uninstall();
        }
        private void Uninstall() {
            if (keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboard);
            if (mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(mouse);
            keyboard = mouse = IntPtr.Zero;
            pending.Clear();
            skipModifiers.Clear();
            modifiersDown.Clear();
        }
        private void Post(Action action) {
            if (action == null || disposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(action); } catch (InvalidOperationException) { }
        }
        private void FinishCapture(Action<Binding> expected, Binding binding) {
            if (capture != expected) return;
            CancelCapture();
            expected(binding);
        }
        private bool Route(Binding binding, bool down) {
            if (!binding.Valid) return false;
            int id = binding.Id;
            bool modifier = binding.Device == Device.Keyboard && Binding.Modifier(binding.Code) != Modifiers.None;
            if (modifier && down) {
                if (!modifiersDown.Add(id)) return false;
                // A modifier alone is interpreted on release, leaving chords available.
                if (modifiersDown.Count > 1) skipModifiers.UnionWith(modifiersDown);
                return false;
            }
            if (!modifier && down) skipModifiers.UnionWith(modifiersDown);
            if (modifier) {
                bool wasDown = modifiersDown.Remove(id);
                if (skipModifiers.Remove(id) || !wasDown || binding.Modifiers != Modifiers.None) return false;
            }
            Pending existing;
            if (pending.TryGetValue(id, out existing)) {
                if (!down) {
                    pending.Remove(id);
                    Post(existing.Fire);
                    if (!enabled && capture == null && pending.Count == 0) Post(TrimHooks);
                }
                return existing.Consume;
            }
            if (!down && !modifier) return false;
            Action fire = null;
            bool consume = false;
            var captured = capture;
            if (captured != null && captureActive()) {
                fire = () => FinishCapture(captured, binding.Stop ? null : binding);
                consume = true;
            } else if (enabled && binding.Stop) {
                pending[id] = new Pending(null, true);
                Post(EmergencyStop);
                return true;
            } else if (enabled && Trigger != null) fire = Trigger(binding, out consume);
            if (fire == null && !consume) return false;
            if (binding.Wheel || modifier) { Post(fire); return consume && !modifier; }
            pending[id] = new Pending(fire, consume);
            return consume;
        }
        private IntPtr Keyboard(int code, IntPtr message, IntPtr data) {
            if (code >= 0) {
                try {
                    var key = (Native.KeyEvent)Marshal.PtrToStructure(data, typeof(Native.KeyEvent));
                    if ((key.Flags & 0x10) == 0) {
                        int vk = key.Code == 13 && (key.Flags & 1) != 0 ? 269 : key.Code;
                        var mods = Native.ReadModifiers() & ~Binding.Modifier(vk);
                        if (Route(new Binding { Device = Device.Keyboard, Code = vk, Modifiers = mods }, (key.Flags & 0x80) == 0)) return new IntPtr(1);
                    }
                } catch (Exception) { }
            }
            return Native.CallNextHookEx(keyboard, code, message, data);
        }
        internal static int MouseCode(int message, uint data) {
            switch (message) {
                case 0x201: case 0x202: return 1;
                case 0x204: case 0x205: return 2;
                case 0x207: case 0x208: return 3;
                case 0x20b: case 0x20c: return (data >> 16) == 1 ? 4 : (data >> 16) == 2 ? 5 : 0;
                case 0x20a: return ((short)(data >> 16)) > 0 ? 6 : ((short)(data >> 16)) < 0 ? 7 : 0;
                default: return 0;
            }
        }
        private IntPtr Mouse(int code, IntPtr message, IntPtr data) {
            if (code >= 0 && message.ToInt32() != 0x200) {
                try {
                    var mouseEvent = (Native.MouseEvent)Marshal.PtrToStructure(data, typeof(Native.MouseEvent));
                    int msg = message.ToInt32();
                    int button = MouseCode(msg, mouseEvent.Data);
                    if ((mouseEvent.Flags & 1) == 0 && button != 0) {
                        bool down = msg != 0x202 && msg != 0x205 && msg != 0x208 && msg != 0x20c;
                        if (Route(new Binding { Device = Device.Mouse, Code = button, Modifiers = Native.ReadModifiers() }, down)) return new IntPtr(1);
                    }
                } catch (Exception) { }
            }
            return Native.CallNextHookEx(mouse, code, message, data);
        }
        public void Dispose() { disposed = true; enabled = false; capture = null; captureActive = null; Uninstall(); }
    }
}
