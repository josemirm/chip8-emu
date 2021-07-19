using System.Drawing;
using System.Windows.Forms;

namespace chip8_emu
{
	public partial class Form1 : Form
	{
		Chip8 c8 = new Chip8();

		public Form1() {
			InitializeComponent();
			Bitmap bmp = c8.GetScreenImage();

			// Adjust windows size to the screen
			pictureBox1.Size = bmp.Size;
			this.Width = pictureBox1.Width + 15;
			this.Height = pictureBox1.Height + 39;
			pictureBox1.Image = bmp;

			c8.ScreenUpdate += Cpu_ScreenUpdateEvent;
		}

		private void Cpu_ScreenUpdateEvent(object sender, System.EventArgs e) {
			pictureBox1.Image = c8.GetScreenImage();
		}

		private void Form1_KeyDown(object sender, KeyEventArgs e) {
			ushort k = Keyboard.Key2ushort(e.KeyCode);
			if (k < 0xFFFF) {
				c8.SetKey(k, true);
			}
		}

		private void Form1_KeyUp(object sender, KeyEventArgs e) {
			ushort k = Keyboard.Key2ushort(e.KeyCode);
			if (k < 0xFFFF) {
				c8.SetKey(k, false);
			}
		}

		private void ClosedEvent(object sender, FormClosedEventArgs e) {
			c8.Dispose();
			c8 = null;
		}

		private void DragEnterEvent(object sender, DragEventArgs e) {
			if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void DragDropEvent(object sender, DragEventArgs e) {
			// Only open the first file
			string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

			c8.ReadFile(files[0]);
			c8.Start();
		}
	}
}
