using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Spiegelwerk.Addin
{
    /// <summary>
    /// Draws the command icon at the sizes SOLIDWORKS asks for (20, 32, 40, 64, 96, 128 px)
    /// into PNG files, so the repository needs no binary image files. The motif: an L-shaped
    /// part on the left, a dashed mirror line, and its independent mirror image on the right.
    /// </summary>
    internal static class IconFactory
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        public static string[] Create()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spiegelwerk", "icons");
            Directory.CreateDirectory(dir);
            var paths = new string[Sizes.Length];
            for (int i = 0; i < Sizes.Length; i++)
            {
                paths[i] = Path.Combine(dir, $"spiegelwerk_{Sizes[i]}.png");
                using Bitmap bmp = Draw(Sizes[i]);
                bmp.Save(paths[i], ImageFormat.Png);
            }
            return paths;
        }

        private static Bitmap Draw(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float u = s / 20f;
            PointF[] left =
            {
                new PointF(2 * u, 3 * u), new PointF(5.5f * u, 3 * u), new PointF(5.5f * u, 13 * u),
                new PointF(8.5f * u, 13 * u), new PointF(8.5f * u, 17 * u), new PointF(2 * u, 17 * u),
            };
            var right = new PointF[left.Length];
            for (int i = 0; i < left.Length; i++) right[i] = new PointF(s - left[i].X, left[i].Y);

            using (var fill = new SolidBrush(Color.FromArgb(255, 47, 111, 179)))
                g.FillPolygon(fill, left);
            using (var fill = new SolidBrush(Color.FromArgb(255, 230, 126, 34)))
                g.FillPolygon(fill, right);
            using (var pen = new Pen(Color.FromArgb(255, 70, 70, 70), Math.Max(1f, u)) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, s / 2f, u, s / 2f, s - u);
            return bmp;
        }
    }
}
