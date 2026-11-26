using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FindClick {
    internal sealed class OcrLine {
        internal readonly List<TextHit> Words = new List<TextHit>();
    }
    internal sealed class Ocr {
        private readonly Type engineType = WinType("Media.Ocr.OcrEngine");
        private readonly Type resultType = WinType("Media.Ocr.OcrResult");
        private readonly Type lineType = WinType("Media.Ocr.OcrLine");
        private readonly Type wordType = WinType("Media.Ocr.OcrWord");
        private readonly Type writerType = WinType("Storage.Streams.DataWriter");
        private readonly Type bitmapType = WinType("Graphics.Imaging.SoftwareBitmap");
        private readonly MethodInfo asTask;
        private readonly MethodInfo makeBitmap;
        private readonly object pixelFormat = Enum.Parse(WinType("Graphics.Imaging.BitmapPixelFormat"), "Bgra8");
        private readonly object alphaMode = Enum.Parse(WinType("Graphics.Imaging.BitmapAlphaMode"), "Ignore");
        private readonly Dictionary<string, object> engines = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        internal readonly int MaxSize;
        internal string Languages { get { return string.Join("、", engines.Keys); } }

        internal Ocr() {
            MaxSize = Convert.ToInt32(Property(engineType, null, "MaxImageDimension"));
            asTask = typeof(System.WindowsRuntimeSystemExtensions).GetMethods().Single(m =>
                m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1").MakeGenericMethod(resultType);
            makeBitmap = bitmapType.GetMethods().Single(m => m.Name == "CreateCopyFromBuffer" && m.GetParameters().Length == 5);
            var languageType = WinType("Globalization.Language");
            foreach (object language in (IEnumerable)Property(engineType, null, "AvailableRecognizerLanguages")) {
                string tag = (string)Property(languageType, language, "LanguageTag");
                object engine = engineType.GetMethod("TryCreateFromLanguage").Invoke(null, new[] { language });
                if (engine != null) engines[tag] = engine;
            }
        }
        private static Type WinType(string name) {
            return Type.GetType("Windows." + name + ", Windows, ContentType=WindowsRuntime", true);
        }
        private static object Property(Type type, object item, string name) {
            return type.GetProperty(name).GetValue(item, null);
        }
        private object ChooseEngine(bool chinese, int pass) {
            string prefix = chinese ? "zh" : "en";
            var ordered = engines.OrderByDescending(e => e.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            return ordered.Count == 0 ? null : ordered[Math.Min(pass, ordered.Count - 1)].Value;
        }
        internal List<OcrLine> Read(Bitmap source, Rectangle capture, bool chinese, bool contrast, int pass, double zoom = 3, bool originalResize = false) {
            var lines = new List<OcrLine>();
            object engine = ChooseEngine(chinese, pass);
            if (engine == null) return lines;
            double scale = Math.Min(zoom, (double)MaxSize / Math.Max(source.Width, source.Height));
            int width = Math.Max(1, (int)(source.Width * scale)), height = Math.Max(1, (int)(source.Height * scale));
            byte[] pixels = new byte[checked(width * height * 4)];
            using (var resized = originalResize ? new Bitmap(source, width, height) : new Bitmap(width, height, PixelFormat.Format32bppArgb)) {
                if (!originalResize) using (var graphics = Graphics.FromImage(resized)) {
                    graphics.InterpolationMode = contrast ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                }
                var locked = resized.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    for (int y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * width * 4, width * 4);
                } finally { resized.UnlockBits(locked); }
            }
            if (contrast) {
                for (int i = 0; i < pixels.Length; i += 4) {
                    int min = Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
                    int max = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
                    byte shade = (byte)(max - min > 45 || max > 148 ? 255 : 0);
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = shade;
                }
            }
            using (var writer = (IDisposable)Activator.CreateInstance(writerType)) {
                writerType.GetMethod("WriteBytes").Invoke(writer, new object[] { pixels });
                var buffer = writerType.GetMethod("DetachBuffer").Invoke(writer, null);
                using (var bitmap = (IDisposable)makeBitmap.Invoke(null, new[] { buffer, pixelFormat, width, height, alphaMode })) {
                    object operation = engineType.GetMethod("RecognizeAsync").Invoke(engine, new object[] { bitmap });
                    var task = (Task)asTask.Invoke(null, new[] { operation });
                    task.GetAwaiter().GetResult();
                    object result = Property(task.GetType(), task, "Result");
                    foreach (object nativeLine in (IEnumerable)Property(resultType, result, "Lines")) {
                        var line = new OcrLine();
                        foreach (object word in (IEnumerable)Property(lineType, nativeLine, "Words")) {
                            object bounds = Property(wordType, word, "BoundingRect");
                            Type boundsType = bounds.GetType();
                            var rect = new RectangleF(
                                capture.X + (float)((double)Property(boundsType, bounds, "X") * source.Width / width),
                                capture.Y + (float)((double)Property(boundsType, bounds, "Y") * source.Height / height),
                                (float)((double)Property(boundsType, bounds, "Width") * source.Width / width),
                                (float)((double)Property(boundsType, bounds, "Height") * source.Height / height));
                            line.Words.Add(new TextHit((string)Property(wordType, word, "Text"), rect, "屏幕识字"));
                        }
                        lines.Add(line);
                    }
                }
            }
            return lines;
        }
    }
}

