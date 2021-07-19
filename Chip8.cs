using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace chip8_emu
{
	class Chip8 : IDisposable
	{
		// -------------------------------- //
		// Internal elements
		// -------------------------------- //

		// Total memory
		private byte[] Memory = new byte[4096];

		// General purpose registers
		private byte[] V = new byte[16];

		// Index register and program counter
		private ushort I, PC;

		// Timers (delay timer and sound timer)
		private byte DT, ST;

		// Stack and stack pointer
		// WARNING: SP goes from 1 to 16 if there's any value in the stack. SP == 0 means empty
		// stack.
		private ushort[] Stack = new ushort[16];
		private ushort SP;

		// Last opcode fetched
		private ushort lastOpcode = 0xFFFF;     // Is initializated at this value for debugging purposes


		// -------------------------------- //
		// Peripherals
		// -------------------------------- //

		private Screen screen = new Screen(Color.LightGreen, Color.DarkGreen, Color.Black, 8, 3, 64, 32);
		private bool[] Keys = new bool[16];

		// -------------------------------- //
		// Others
		// -------------------------------- //

		// Variables to store temporally the 'x' and 'y' vector positions, kk value, and nnn value
		// to use them inside the instructions.
		private ushort x, y, kk, nnn, n;

		// Random number generation
		private Random RNG = new Random();

		// Position of hex number sprites in memory
		private const ushort HEXFONT_POS = 0;
		private const ushort HEXFONT_SIZE = 5;

		// Running control
		bool running = false;
		private Thread ExecutionThread;
		
		// Async updating of timers
		private Thread TimersThread;
		private Mutex TimersMtx;

		// This class is disposable to make easier to stop the threads
		bool _disposed = false;

		public Chip8() {
			// Reset all components to their default value
			Reset();

			// Create a mutex to update the timers
			TimersMtx = new Mutex();
		}



		public Bitmap GetScreenImage() {
			return screen.Bitmap;
		}



		public void ReadFile(string fname) {
			Stop();
			Reset();

			if (File.Exists(fname)) {
				using (BinaryReader reader = new BinaryReader(File.Open(fname, FileMode.Open))) {
					byte[] fileData = reader.ReadBytes(Memory.Length - 0x200);
					fileData.CopyTo(Memory, 0x200);
				}
			} else {
				MessageBox.Show($"File {fname} does not exist.");
			}
		}


		public void Start() {
			if (!running) {
				TimersThread = new Thread(UpdateTimers);
				TimersThread.Start();

				running = true;
				ExecutionThread = new Thread(Execution);
				ExecutionThread.Start();
			}
		}

		public void Stop() {
			if (running) {
				running = false;
				ExecutionThread.Join();
				TimersThread.Join();
			}
		}


		private void Reset() {
			// Initializate registers and memory
			PC = 0x200; // All CHIP-8 programs starts at 0x200
			I = 0;
			SP = 0;
			DT = 0;
			ST = 0;

			// Clear the display
			screen.ClearScreen();
			ScreenUpdate?.Invoke(this, EventArgs.Empty);

			// Clear key pressed
			for (int i = 0; i < Keys.Length; ++i) {
				Keys[i] = false;
			}

			// Clear registers
			for (int i = 0; i < 16; ++i) {
				V[i] = 0;
			}

			// Clear memory
			for (int i = 0; i < 4096; ++i) {
				Memory[i] = 0;
			}

			// Copy numerical characters to memory starting at HEXFONT_POS
			for (int i = 0; i < HexFontset.Length; ++i) {
				Memory[i + HEXFONT_POS] = HexFontset[i];
			}
		}
		
		public event EventHandler ScreenUpdate;


		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) {
			if (_disposed) {
				return;
			}

			if (disposing) {
				Stop();
			}

			_disposed = true;
		}


		private void Execution() {
			while (running) {
				// Fetch opcode (composed of two bytes)
				lastOpcode = Memory[PC];
				lastOpcode <<= 8;
				lastOpcode |= Memory[PC + 1];

				// Decode opcode
				Action opcodeFunction = DecodeOpcode();
				GetOpcodeValues();

				// Execute opcode
				opcodeFunction();

				// Timers are updated asyncronously

				// Moves to the next instruction
				PC += 2;
			}

			Thread.Sleep(25);
		}


		public void SetKey(ushort key, bool value) {
			Keys[key] = value;
		}


		private void UpdateTimers() {
			while (running) {
				TimersMtx.WaitOne();
				// TODO: This should make or stop a sound (a tone)
				// From undocumented RCA: 0x01 won't sound, but 0x02 or bigger will do.
				if (ST > 0) {
					ST--;
				}

				if (DT > 0) {
					DT--;
				}

				TimersMtx.ReleaseMutex();
				Thread.Sleep(16);
			}
		}


		private byte ReadCurrentKey() {
			// This only pick up the first key pressed in numerical order
			for (int i = 0; i < Keys.Length; ++i) {
				if (Keys[i] == true) {
					return (byte)i;
				}
			}

			// If there isn't any key pressed, returns an invalid key value. This should work with
			// any value different from 0 to 15.
			return 0xFF;
		}

		private byte WaitKeyEvent() {
			// TODO: Needed to read pressed key
			System.Diagnostics.Trace.Write("'WaitKeyEvent' isn't implemented");


			Thread.Sleep(100);
			return 0;
		}


		private void StackError(int pos) {
			Trace.Fail($"Stack error trying to reach {pos} position");
		}


		private Action DecodeOpcode() {
			if (lastOpcode == 0x00E0) return Op_CLS;
			if (lastOpcode == 0x00EE) return Op_RET;
			if (lastOpcode <= 0x0FFF) return Op_SYS;
			if (lastOpcode <= 0x1FFF) return Op_JP_addr;
			if (lastOpcode <= 0x2FFF) return Op_CALL_addr;
			if (lastOpcode <= 0x3FFF) return Op_SE_Vx_byte;
			if (lastOpcode <= 0x4FFF) return Op_SNE_Vx_byte;
			if (lastOpcode <= 0x5FFF) return Op_SE_Vx_Vy;
			if (lastOpcode <= 0x6FFF) return Op_LD_Vx_byte;
			if (lastOpcode <= 0x7FFF) return Op_ADD_Vx_byte;

			if ((lastOpcode & 0xF00F) == 0x8000) return Op_LD_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8001) return Op_OR_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8002) return Op_AND_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8003) return Op_XOR_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8004) return Op_ADD_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8005) return Op_SUB_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8006) return Op_SHR_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x8007) return Op_SUBN_Vx_Vy;
			if ((lastOpcode & 0xF00F) == 0x800E) return Op_SHL_Vx_Vy;

			// The rest of opcodes between 0x8000 and 0x8FFF that doesn't match with the previous
			// ones are unknown
			if ((lastOpcode & 0xF000) == 0x8000) return Op_UNKNOWN;

			if (lastOpcode <= 0x9FFF) return Op_SNE_Vx_Vy;
			if (lastOpcode <= 0xAFFF) return Op_LD_I_addr;
			if (lastOpcode <= 0xBFFF) return OP_JP_V0_addr;
			if (lastOpcode <= 0xCFFF) return OP_RND_Vx_byte;
			if (lastOpcode <= 0xDFFF) return Op_DRW_Vx_Vy_nibble;

			if ((lastOpcode & 0xF0FF) == 0xE09E) return Op_SKP_Vx;
			if ((lastOpcode & 0xF0FF) == 0xE0A1) return Op_SKNP_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF007) return Op_LD_Vx_DT;
			if ((lastOpcode & 0xF0FF) == 0xF00A) return Op_LD_Vx_K;
			if ((lastOpcode & 0xF0FF) == 0xF015) return Op_LD_DT_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF018) return Op_LD_ST_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF01E) return Op_ADD_I_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF029) return Op_LD_F_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF033) return Op_LD_B_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF055) return Op_LD_I_Vx;
			if ((lastOpcode & 0xF0FF) == 0xF065) return Op_LD_Vx_I;

			return Op_UNKNOWN;
		}


		private void GetOpcodeValues() {
			x = lastOpcode;
			x &= 0x0F00;
			x >>= 8;

			y = lastOpcode;
			y &= 0x00F0;
			y >>= 4;

			kk = lastOpcode;
			kk &= 0x00FF;

			nnn = lastOpcode;
			nnn &= 0x0FFF;

			n = nnn;
			n &= 0x00F;
		}


		private void Op_UNKNOWN() {
			Trace.Fail($"Unrecognized opcode: 0x{lastOpcode.ToString("X")}");
		}

		private void Op_SYS() {
			// 0nnn - Jump to a machine code routine at nnn
			PC = nnn;

			PC -= 2;
		}

		private void Op_CLS() {
			// 00E0 - Clears the screen
			screen.ClearScreen();
		}

		private void Op_RET() {
			// 00EE - Return to subroutine
			if (SP == 0) {
				StackError(-1);
			} else {
				PC = Stack[SP - 1];
				SP--;
			}

			// This is to correct the increment that will be done up next
			//SP -= 2;
		}

		private void Op_JP_addr() {
			// 1nnn - Jumps to nnn
			// This is done this way insted of PC = lastOpcode & 0x0FFF to avoid C# casting
			// automatically to int. This also happens in next opcodes.
			PC = nnn;

			PC -= 2;
		}

		private void Op_CALL_addr() {
			// 2nnn - Call subroutine at nnn
			// TODO: Arreglar ésto
			if (SP >= Stack.Length) {
				StackError(SP);
			} else {
				SP++;
				Stack[SP - 1] = PC;
				PC = nnn;
			}
			
			PC -= 2;
		}

		private void Op_SE_Vx_byte() {
			// 3xkk - Skip next instruction if Vx == kk
			if (V[x] == kk) {
				PC += 2;
			}
		}

		private void Op_SNE_Vx_byte() {
			// 4xkk - Skip next instruction if Vx != kk
			if (V[x] != kk) {
				PC += 2;
			}
		}

		private void Op_SE_Vx_Vy() {
			// 5xy0 - Skip next instruction if Vx == Vy
			if (V[x] == V[y]) {
				PC += 2;
			}
		}

		private void Op_LD_Vx_byte() {
			// 6xkk - Set Vx = kk
			V[x] = (byte)kk;
		}


		private void Op_ADD_Vx_byte() {
			// 7xkk - Set Vx = Vx + kk
			V[x] += (byte)kk;
		}

		private void Op_LD_Vx_Vy() {
			// 8xy0 - Set Vx = Vy			
			V[x] = V[y];
		}

		private void Op_OR_Vx_Vy() {
			// 8xy1 - Set Vx = Vx OR Vy
			V[x] |= V[y];
		}

		private void Op_AND_Vx_Vy() {
			// 8xy2 - Set Vx = Vx AND Vy
			V[x] &= V[y];
		}

		private void Op_XOR_Vx_Vy() {
			// 8xy3 - Set Vx = Vx XOR Vy
			V[x] ^= V[y];
		}

		private void Op_ADD_Vx_Vy() {
			// 8xy4 - Set Vx = Vx + Vy, VF = carry
			if ((V[x] + V[y]) > 0xFF) {
				V[0x0F] = 1;
			} else {
				V[0x0F] = 0;
			}

			V[x] += V[y];
		}

		private void Op_SUB_Vx_Vy() {
			// 8xy5 - Set Vx = Vx - Vy, VF = {Vx > Vy: 1, otherwise: 0}
			if (V[x] > V[y]) {
				V[0x0F] = 1;
			} else {
				V[0x0F] = 0;
			}

			V[x] -= V[y];
		}

		private void Op_SHR_Vx_Vy() {
			// 8xy6 - Set Vx = Vy >> 1. VF = LSB(Vy)
			V[0x0F] = V[y];
			V[0x0F] &= 0x0001;

			V[x] = V[y];
			V[x] >>= 1;

		}

		private void Op_SUBN_Vx_Vy() {
			// 8xy7 - Set Vx = Vy - Vx, Vf = {Vy > Vx: 1, otherwise: 0}
			byte tmp;

			if (V[y] > V[x]) {
				V[0x0F] = 1;
			} else {
				V[0x0F] = 0;
			}

			tmp = V[y];
			tmp -= V[x];
			V[x] = tmp;
		}

		private void Op_SHL_Vx_Vy() {
			// 8xyE - Set Vx = Vy << 1. VF = MSB(Vy)
			V[0x0F] = V[y];
			V[0x0F] >>= 7;

			V[x] = V[y];
			V[x] <<= 1;
		}

		private void Op_SNE_Vx_Vy() {
			// 9xy0 - Skip next instruction if Vx != Vy
			if (V[x] != V[y]) {
				PC += 2;
			}
		}

		private void Op_LD_I_addr() {
			// Annn - Set I = nnn
			I = lastOpcode;
			I &= 0x0FFF;
		}

		private void OP_JP_V0_addr() {
			// Bnnn - Jump to nnn + V0
			PC = lastOpcode;
			PC &= 0x0FFF;
			PC += V[0];

			PC -= 2;
		}

		private void OP_RND_Vx_byte() {
			// Cxkk - Set Vx = random byte AND kk
			byte[] rnd = new byte[1];
			RNG.NextBytes(rnd);

			V[x] = rnd[0];
			V[x] &= (byte)kk;
		}

		private void Op_DRW_Vx_Vy_nibble() {
			// Dxyn - Display a 8 pixels wide sprite of n pixels of height starting at memory
			// location I at (Vx, Vy) doing a XOR operation with the previoys image.
			// Sets VF = {Collision} when there are any collisions (1 XOR 1 == 0).

			// The interpreter reads n bytes from memory, starting at the address stored in I.
			// These bytes are then displayed as sprites on screen at coordinates(Vx, Vy).
			// Sprites are XORed onto the existing screen.
			//
			// If this causes any pixels to be erased, VF is set to 1, otherwise it is set to 0.
			//
			// If the sprite is positioned so part of it is outside the coordinates of the display,
			// it wraps around to the opposite side of the screen.

			if (screen.XORSprite(Memory, I, V[x], V[y], n)) {
				// When a collision is detected
				V[0xF] = 1;
			} else {
				V[0xF] = 0;
			}

			ScreenUpdate?.Invoke(this, EventArgs.Empty);
		}

		private void Op_SKP_Vx() {
			// Ex9E - Skip next instruction if key with the value of Vx is pressed
			byte key = ReadCurrentKey();

			if (V[x] == key) {
				PC += 2;
			}
		}

		private void Op_SKNP_Vx() {
			// ExA1 - Skip next instruction if key with the value of Vx is NOT pressed
			byte key = ReadCurrentKey();

			if (V[x] != key) {
				PC += 2;
			}
		}

		private void Op_LD_Vx_DT() {
			TimersMtx.WaitOne();
			V[x] = DT;
			TimersMtx.ReleaseMutex();
		}

		private void Op_LD_Vx_K() {
			// Fx0A - Wait for keypress and stores its value in Vx
			V[x] = WaitKeyEvent();
		}

		private void Op_LD_DT_Vx() {
			// Fx15 - DT = Vx
			TimersMtx.WaitOne();
			DT = V[x];
			TimersMtx.ReleaseMutex();
		}

		private void Op_LD_ST_Vx() {
			// Fx18 - ST = Vx
			TimersMtx.WaitOne();
			ST = V[x];
			TimersMtx.ReleaseMutex();
		}

		private void Op_ADD_I_Vx() {
			// Fx1E - I = I + Vx
			I += V[x];
		}

		private void Op_LD_F_Vx() {
			// Fx29 - I = {Location of sprite for digit Vx}
			I &= 0x000F;        // Only pick the first digit
			I *= HEXFONT_SIZE;  // Add a displacement depending of the number of bytes needed
			I += HEXFONT_POS;   // Add a displacement depending of the starting point
		}

		private void Op_LD_B_Vx() {
			// Fx33 - Store BCD representation of Vx in memory locations I, I+1, and I+2.

			// Takes the decimal value of Vx, and places the hundreds digit in memory at location
			// in I, the tens digit at location I+1, and the ones digit at location I + 2.
			ushort num = V[x];
			Memory[I + 2] = (byte)(num % 10);
			num /= 10;
			Memory[I + 1] = (byte)(num % 10);
			num /= 10;
			Memory[I] = (byte)num;
		}

		private void Op_LD_I_Vx() {
			// Fx55 - Stores from V0 to Vx in memory starting at I
			// Some emulators run this instruction with I = I + x + 1 at the end
			for (int i = 0; i < x; ++i) {
				Memory[I + i] = V[i];
			}
		}

		private void Op_LD_Vx_I() {
			// Fx65 - Write to registers V0 to Vx from memory starting at I
			// Some emulators run this instruction with I = I + x + 1 at the end
			for (int i = 0; i < x; ++i) {
				V[i] = Memory[I + i];
			}
		}


		// -------------------------------- //
		// Constant fontset for hex values
		// -------------------------------- //

		private static readonly Byte[] HexFontset = {
			0xF0, 0x90, 0x90, 0x90, 0xF0, // 0
			0x20, 0x60, 0x20, 0x20, 0x70, // 1
			0xF0, 0x10, 0xF0, 0x80, 0xF0, // 2
			0xF0, 0x10, 0xF0, 0x10, 0xF0, // 3
			0x90, 0x90, 0xF0, 0x10, 0x10, // 4
			0xF0, 0x80, 0xF0, 0x10, 0xF0, // 5
			0xF0, 0x80, 0xF0, 0x90, 0xF0, // 6
			0xF0, 0x10, 0x20, 0x40, 0x40, // 7
			0xF0, 0x90, 0xF0, 0x90, 0xF0, // 8
			0xF0, 0x90, 0xF0, 0x10, 0xF0, // 9
			0xF0, 0x90, 0xF0, 0x90, 0x90, // A
			0xE0, 0x90, 0xE0, 0x90, 0xE0, // B
			0xF0, 0x80, 0x80, 0x80, 0xF0, // C
			0xE0, 0x90, 0x90, 0x90, 0xE0, // D
			0xF0, 0x80, 0xF0, 0x80, 0xF0, // E
			0xF0, 0x80, 0xF0, 0x80, 0x80  // F
		};

	}
}
