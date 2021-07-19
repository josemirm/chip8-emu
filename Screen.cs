using System;
using System.Drawing;
using System.Threading;

namespace chip8_emu
{
	class Screen
	{
		private Color onColor, offColor, bgColor;
		private Brush onBrush, offBrush;

		private int pixelSize, pixelSeparation;
		private int width, height;

		private bool[,] pixelValue;
		Rectangle pixelRec = new Rectangle();
		private Graphics graph;
		private Bitmap mainBmp;
		private Mutex mtx = new Mutex();

		public Screen(Color onColor, Color offColor, Color bgColor, int pixelSize, int pixelSeparation, int numPixelsWidth, int numPixelsHeight) {
			this.onColor = onColor;
			this.offColor = offColor;
			this.bgColor = bgColor;

			onBrush = new SolidBrush(onColor);
			offBrush = new SolidBrush(offColor);

			this.pixelSize = pixelSize;
			this.pixelSeparation = pixelSeparation;

			width = numPixelsWidth;
			height = numPixelsHeight;

			pixelValue = new bool[numPixelsWidth, numPixelsHeight];
			for (int x = 0; x < numPixelsWidth; ++x) {
				for (int y = 0; y < numPixelsHeight; ++y) {
					pixelValue[x, y] = false;
				}
			}

			// Having this example, with pixels in dashes ('-') and spaces in bars ('|'), we have:
			//
			// |||||||||||||||||||||||||||||||||||||
			// ||-----||-----||-----||-----||-----||
			// ||-----||-----||-----||-----||-----||
			// ||-----||-----||-----||-----||-----||
			// ||-----||-----||-----||-----||-----||
			// |||||||||||||||||||||||||||||||||||||
			// |||||||||||||||||||||||||||||||||||||
			// ||-----||-----||-----||-----||-----||
			// ||-----||-----||-----||-----||-----||
			// ||-----||-----||-----||-----||-----||
			// ||-----||-----||-----||-----||-----||
			// |||||||||||||||||||||||||||||||||||||
			//
			// 4 x 2 virtual pixels with 2 real pixels space between them.
			//
			// So, the total real pixel needed are:
			// (VP: Virtual Pixels, Sep: Separation between virtual pixels)
			// - Width = num_VP_width * (VP.Width + Sep) + Sep
			// - Height = num_VP_height * (VP.Height + Sep) + Sep

			int bmpWidth = numPixelsWidth * (pixelSize + pixelSeparation) + pixelSeparation;
			int bmpHeight = numPixelsHeight * (pixelSize + pixelSeparation) + pixelSeparation;
			mainBmp = new Bitmap(bmpWidth, bmpHeight);

			// Creates a Graphics object to manipulate the bitmap and draws the background color
			graph = Graphics.FromImage(mainBmp);
			graph.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
			graph.Clear(bgColor);
			
			UpdateBitmap();
		}

		private void UpdateBitmap() {
			mtx.WaitOne();

			for (int x = 0; x < width; ++x) {
				for (int y = 0; y < height; ++y) {
					pixelRec.X = x * (pixelSize + pixelSeparation) + pixelSeparation;
					pixelRec.Y = y * (pixelSize + pixelSeparation) + pixelSeparation;
					pixelRec.Width = pixelSize;
					pixelRec.Height = pixelSize;

					if (pixelValue[x,y] == true) {
						graph.FillRectangle(onBrush, pixelRec);
					} else {
						graph.FillRectangle(offBrush, pixelRec);
					}
				}
			}
			
			mtx.ReleaseMutex();
		}

		private int GetPixelPosX(int x) {
			return x * (pixelSize + pixelSeparation) + pixelSeparation;
		}

		private int GetPixelPosY(int y) {
			return y * (pixelSize + pixelSeparation) + pixelSeparation;
		}

		public Bitmap Bitmap {
			get {
				mtx.WaitOne();
				Bitmap outBmp = mainBmp;
				mtx.ReleaseMutex();

				return outBmp;
			}
		}


		public void SetPixel(int x, int y, bool value) {
			pixelValue[x, y] = value;

			// Also, updates the pixel in the bitmap
			Rectangle pixelRec = new Rectangle(GetPixelPosX(x), GetPixelPosY(y), pixelSize, pixelSize);

			if (pixelValue[x, y] == true) {
				graph.FillRectangle(onBrush, pixelRec);
			} else {
				graph.FillRectangle(offBrush, pixelRec);
			}
		}


		public void ClearScreen() {
			for (int x = 0; x < width; ++x) {
				for (int y = 0; y < height; ++y) {
					pixelValue[x, y] = false;
				}
			}

			UpdateBitmap();
		}


		public bool XORSprite(byte[] mem, int offset, int x, int y, int hztalLines) {
			const int SPRITE_WIDTH = 8;
			bool collision = false;
			int byteCount = 0;

			for (int line = 0; line < hztalLines; ++line) {
				byte currLine = mem[offset + byteCount];
				byteCount++;

				for (int i=0; i<SPRITE_WIDTH; ++i) {
					bool bit = ((currLine >> (7 - i)) & 1) == 1;
					int pos_x = x + i;
					int pos_y = y + line;

					if (pos_x >= width) {
						pos_x -= width;
					}

					if (pos_y >= height) {
						pos_y -= height;
					}

					// It seems there's no need to check when X or Y position goes below zero.

					collision |= pixelValue[pos_x, pos_y] & bit;

					pixelValue[pos_x, pos_y] ^= bit;
				}
			}

			UpdateBitmap();

			return collision;
		}

		public void PositionMatrix() {
			int mid_x = width  / 2 - 1;
			int mid_y = height / 2 - 1;

			SetPixel(		 0,			 0, true);
			SetPixel(		 0,		 mid_y, true);
			SetPixel(		 0, height - 1, true);

			SetPixel(	 mid_x,			0,	true);
			SetPixel(	 mid_x,		mid_y,	true);
			SetPixel(	 mid_x, height - 1, true);

			SetPixel(width - 1,			0,	true);
			SetPixel(width - 1,		 mid_y,	true);
			SetPixel(width - 1, height - 1, true);
		}
	}
}
