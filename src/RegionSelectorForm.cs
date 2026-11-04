using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DrawingRectangle = System.Drawing.Rectangle;

namespace IslandShot
{
    internal sealed class RegionSelectorForm : Form
    {
        readonly Bitmap background;
        Point start;
        DrawingRectangle selection;
        bool selecting;
        DrawingRectangle? result;

        public RegionSelectorForm(Bitmap background)
        {
            this.background = background;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;
        }

        public DrawingRectangle? Pick()
        {
            ShowDialog();
            return result;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(background, ClientRectangle);
            using (var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(shade, ClientRectangle);
            }
            if (selection.Width > 0 && selection.Height > 0)
            {
                using (var clear = new SolidBrush(Color.Transparent))
                {
                    e.Graphics.FillRectangle(clear, selection);
                }
                using (var pen = new Pen(Color.FromArgb(70, 225, 255), 1))
                {
                    e.Graphics.DrawRectangle(pen, selection.Left, selection.Top, selection.Width, selection.Height);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            start = e.Location;
            selection = new DrawingRectangle(start.X, start.Y, 0, 0);
            selecting = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!selecting) return;
            int x = Math.Min(start.X, e.X);
            int y = Math.Min(start.Y, e.Y);
            selection = new DrawingRectangle(x, y, Math.Abs(e.X - start.X), Math.Abs(e.Y - start.Y));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!selecting) return;
            selecting = false;
            if (selection.Width < 4 || selection.Height < 4)
            {
                result = null;
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            result = new DrawingRectangle(Bounds.Left + selection.Left, Bounds.Top + selection.Top, selection.Width, selection.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                result = null;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
