using System;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FindClick {
    internal static class Native {
        internal const int MouseHook = 14, KeyboardHook = 13;
        [StructLayout(LayoutKind.Sequential)] internal struct KeyEvent {
            internal int Code, Scan, Flags, Time;
            internal UIntPtr Extra;
        }
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct Pt {
            internal int X, Y;
            internal Pt(Point p) { X = p.X; Y = p.Y; }
            internal Point Point { get { return new Point(X, Y); } }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Bounds {
            internal int Left, Top, Right, Bottom;
            internal Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseEvent {
            internal Pt Position;
            internal uint Data, Flags, Time;
            internal UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput {
            internal int X, Y;
            internal uint Data, Flags, Time;
            internal UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KeyInput {
            internal ushort Key, Scan;
            internal uint Flags, Time;
            internal UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Explicit)] private struct InputData {
            [FieldOffset(0)] internal MouseInput Mouse;
            [FieldOffset(0)] internal KeyInput Key;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Input {
            internal uint Type;
            internal InputData Data;
        }
        [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Pt pt);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Bounds bounds);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref Pt pt);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Pt pt);
        [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, ref Input inputs, int size);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        private static readonly int InputSize = Marshal.SizeOf(typeof(Input));
        internal static IntPtr Foreground() { return GetForegroundWindow(); }
        internal static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static Modifiers ReadModifiers() {
            var mods = Modifiers.None;
            if (Down(0x10)) mods |= Modifiers.Shift;
            if (Down(0x11)) mods |= Modifiers.Ctrl;
            if (Down(0x12)) mods |= Modifiers.Alt;
            if (Down(0x5b) || Down(0x5c)) mods |= Modifiers.Win;
            return mods;
        }
        internal static string Title(IntPtr window) {
            var text = new StringBuilder(1024);
            return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "";
        }
        internal static uint ProcessOf(IntPtr window) {
            uint process;
            GetWindowThreadProcessId(window, out process);
            return process;
        }
        internal static Rectangle CaptureBounds(IntPtr window) {
            Bounds client;
            var origin = new Pt();
            if (!GetClientRect(window, out client) || !ClientToScreen(window, ref origin)) return Rectangle.Empty;
            return Rectangle.Intersect(new Rectangle(origin.X, origin.Y, client.Right, client.Bottom),
                System.Windows.Forms.SystemInformation.VirtualScreen);
        }
        internal static bool CanClick(IntPtr window, Point point) {
            var under = WindowFromPoint(new Pt(point));
            return under != IntPtr.Zero && GetAncestor(under, 2) == window && GetForegroundWindow() == window;
        }
        internal static bool ClientPoint(IntPtr window, Point coordinates, out Point screen) {
            Bounds bounds;
            var point = new Pt(coordinates);
            screen = Point.Empty;
            if (!GetClientRect(window, out bounds) || !bounds.Rectangle.Contains(coordinates) || !ClientToScreen(window, ref point)) return false;
            screen = point.Point;
            return CanClick(window, screen);
        }
        internal static bool Move(IntPtr window, Point point) {
            return CanClick(window, point) && SetCursorPos(point.X, point.Y) && CanClick(window, point);
        }
        private static Input Encode(Device device, int code, bool up) {
            if (device == Device.Keyboard) {
                uint scan = MapVirtualKey((uint)(code == 269 ? 13 : code), 4);
                uint flags = up ? 2u : 0u;
                if (code == 269 || (scan >> 8) == 0xe0) flags |= 1;
                return new Input { Type = 1, Data = new InputData { Key = new KeyInput { Key = (ushort)(code == 269 ? 13 : code), Flags = flags } } };
            }
            uint mouseFlags = 0, mouseData = 0;
            switch (code) {
                case 1: mouseFlags = up ? 0x0004u : 0x0002u; break;
                case 2: mouseFlags = up ? 0x0010u : 0x0008u; break;
                case 3: mouseFlags = up ? 0x0040u : 0x0020u; break;
                case 4: case 5: mouseFlags = up ? 0x0100u : 0x0080u; mouseData = (uint)(code - 3); break;
                case 6: case 7: mouseFlags = 0x0800; mouseData = code == 6 ? 120u : unchecked((uint)-120); break;
            }
            return new Input { Data = new InputData { Mouse = new MouseInput { Flags = mouseFlags, Data = mouseData } } };
        }
        private static bool Send(Input input) { return SendInput(1, ref input, InputSize) == 1; }
        internal sealed class PressedInput : IDisposable {
            private readonly List<Binding> sent = new List<Binding>(5);
            private static readonly int[] ModifierKeys = { 0xa2, 0xa0, 0xa4, 0x5b };
            private static readonly int[] MouseKeys = { 0, 1, 2, 4, 5, 6 };
            internal void Press(Binding binding, Action check) {
                foreach (int code in ModifierKeys) {
                    var modifier = Binding.Modifier(code);
                    if ((binding.Modifiers & modifier) != 0 && (ReadModifiers() & modifier) == 0) {
                        check();
                        SendDown(new Binding { Device = Device.Keyboard, Code = code });
                    }
                }
                check();
                SendDown(binding);
            }
            internal void Release(Binding binding) {
                int index = sent.FindLastIndex(item => item.Id == binding.Id);
                if (index < 0) throw new MacroException(UiText.F("找不到要松开的「{0}」", "‘{0}’ is not held", binding));
                if (!Send(Encode(binding.Device, binding.Code, true))) throw new MacroException(UiText.T("Windows 未接收松开动作", "Windows did not accept the release"));
                sent.RemoveAt(index);
            }
            private void SendDown(Binding binding) {
                if (!binding.Wheel) {
                    int key = binding.Device == Device.Keyboard ? (binding.Code == 269 ? 13 : binding.Code) :
                        MouseKeys[binding.Code];
                    if (Down(key)) throw new MacroException(UiText.F("「{0}」已按住，请先松开", "‘{0}’ is already held; release it first", binding));
                }
                if (!Send(Encode(binding.Device, binding.Code, false))) throw new MacroException(UiText.T("Windows 未接收键鼠输入", "Windows did not accept the input"));
                if (!binding.Wheel) sent.Add(binding);
            }
            public void Dispose() {
                for (int i = sent.Count - 1; i >= 0; i--) {
                    var release = Encode(sent[i].Device, sent[i].Code, true);
                    if (!Send(release)) Send(release);
                }
                sent.Clear();
            }
        }
        internal static void RestoreIfUnmoved(Pt original, Point last) {
            Pt current;
            if (GetCursorPos(out current) && current.Point == last) SetCursorPos(original.X, original.Y);
        }
    }
}
