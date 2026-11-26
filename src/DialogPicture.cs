using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace FindClick {
    internal static class DialogPicture {
        private sealed class Edge {
            internal int X, Y, Width, Color;
        }
        private static bool Dark(int pixel) {
            return Math.Min(pixel & 255, Math.Min((pixel >> 8) & 255, (pixel >> 16) & 255)) < 191;
        }
        private static bool Similar(int a, int b) {
            return Math.Abs((a & 255) - (b & 255)) <= 12 &&
                Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) <= 12 &&
                Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) <= 12;
        }
        // Detect standard Qt frames from their visible borders. Coordinates are relative to the bitmap.
        internal static List<Rectangle> FindFrames(Bitmap image) {
            var frames = new List<Rectangle>();
            if (image.Width > 1600 || image.Height > 1300 || image.Width < 80 || image.Height < 65) return frames;
            var pixels = new int[image.Width * image.Height];
            var locked = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                for (int y = 0; y < image.Height; y++) Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * image.Width, image.Width);
            } finally { image.UnlockBits(locked); }
            var edges = new List<Edge>();
            for (int y = 0; y < image.Height; y++) {
                int row = y * image.Width;
                for (int x = 0; x < image.Width;) {
                    int x1 = x++;
                    int color = pixels[row + x1];
                    if (!Dark(color)) continue;
                    while (x < image.Width && Similar(color, pixels[row + x])) x++;
                    if (x - x1 >= 34) edges.Add(new Edge { X = x1, Y = y, Width = x - x1, Color = color });
                }
            }
            if (edges.Count > 400) return frames;
            for (int i = 0; i < edges.Count; i++) {
                var top = edges[i];
                for (int j = i + 1; j < edges.Count; j++) {
                    var bottom = edges[j];
                    int height = bottom.Y - top.Y + 1;
                    if (height < 15 || Math.Abs(top.X - bottom.X) > 1 || Math.Abs(top.Width - bottom.Width) > 2) continue;
                    if (!Similar(top.Color, bottom.Color)) continue;
                    int right = top.X + top.Width - 1;
                    int good = 0;
                    for (int sample = 1; sample <= 6; sample++) {
                        int row = (top.Y + sample * (height - 1) / 7) * image.Width;
                        if ((Similar(top.Color, pixels[row + top.X]) || Similar(top.Color, pixels[row + top.X + 1])) &&
                            (Similar(top.Color, pixels[row + right]) || Similar(top.Color, pixels[row + right - 1]))) good++;
                    }
                    if (good < 5) continue;
                    var frame = new Rectangle(top.X, top.Y, top.Width, height);
                    if (!frames.Any(r => Math.Abs(r.Left - frame.Left) <= 2 && Math.Abs(r.Right - frame.Right) <= 2 &&
                        Math.Abs(r.Top - frame.Top) <= 2 && Math.Abs(r.Bottom - frame.Bottom) <= 2)) frames.Add(frame);
                }
            }
            return frames;
        }
    }
}
