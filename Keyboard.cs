using System;
using System.Windows.Forms;

namespace chip8_emu
{
	class Keyboard
	{
		public static ushort Key2ushort(Keys k) => k switch {
			Keys.D1 => 0x1,
			Keys.D2 => 0x2,
			Keys.D3 => 0x3,
			Keys.D4 => 0xC,

			Keys.Q => 0x4,
			Keys.W => 0x5,
			Keys.E => 0x6,
			Keys.R => 0xD,

			Keys.A => 0x7,
			Keys.S => 0x8,
			Keys.D => 0x9,
			Keys.F => 0xE,

			Keys.Z => 0xA,
			Keys.X => 0x0,
			Keys.C => 0xB,
			Keys.V => 0xF,

			_ => 0xFFFF
		};
	}
	
}