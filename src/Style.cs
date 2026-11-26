using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FindClick {
    internal static class Style {
        internal static readonly Color Ink = Color.FromArgb(22, 34, 52), Muted = Color.FromArgb(78, 96, 119);
        internal static readonly Color Background = Color.FromArgb(246, 248, 252), Border = Color.FromArgb(215, 225, 239);
        internal static readonly Color Blue = Color.FromArgb(24, 75, 204), LightBlue = Color.FromArgb(225, 237, 255);
        // These shared fonts live for the lifetime of the WinForms message loop.
        private static readonly Dictionary<float, Font> Fonts = new Dictionary<float, Font>();
        internal static Font Font(float size, bool bold) {
            float key = bold ? -size : size;
            Font font;
            if (!Fonts.TryGetValue(key, out font)) Fonts[key] = font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            return font;
        }
        internal static void ReleaseFonts() {
            foreach (var font in Fonts.Values) font.Dispose();
            Fonts.Clear();
        }
        internal static Button Button(string text, int width, bool primary) {
            var button = new Button { Text = text, Width = width, Height = 35, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(21, 63, 174) : LightBlue;
            return button;
        }
        internal static Label Label(string text, float fontSize, bool bold, Color color) {
            return new Label { Text = text, Font = Font(fontSize, bold), ForeColor = color, AutoSize = true, Anchor = AnchorStyles.Left };
        }
        internal static void IconFor(Form form) {
            try {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon == null) return;
                form.Icon = icon;
                form.Disposed += delegate { icon.Dispose(); };
            } catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
