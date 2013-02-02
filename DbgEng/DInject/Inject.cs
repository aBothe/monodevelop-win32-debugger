
// by Alexander Bothe (info@alexanderbothe.com)

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Extemory;
using Extemory.Win32;

namespace DHijack
{
	public class Inject
	{
		#region Execution
		public static void ExecuteFunction(Process p, IntPtr functionAddress, uint timeout = (uint)ThreadWaitValue.Infinite)
		{
			var hThread = Imports.CreateRemoteThread(p.GetHandle(), IntPtr.Zero, 0, functionAddress, IntPtr.Zero, 0, IntPtr.Zero);
			if (hThread == IntPtr.Zero)
				throw new Win32Exception(Marshal.GetLastWin32Error());

			var singleObject = Imports.WaitForSingleObject(hThread, timeout);
			if (!(singleObject == (uint)ThreadWaitValue.Object0 || singleObject == (uint)ThreadWaitValue.Timeout))
				throw new Win32Exception(Marshal.GetLastWin32Error());
		}

		public static string ReadDString(Process p, IntPtr stringAddress)
		{
			uint stringLength = p.Read<uint>(stringAddress);
			var firstChar = p.Read<IntPtr>(stringAddress + 4);

			return p.ReadString(firstChar, Encoding.UTF8, (int)stringLength);
		}

		public static void FreeDString(Process p, IntPtr stringAddress)
		{
			uint stringLength = p.Read<uint>(stringAddress);
			var firstChar = p.Read<IntPtr>(stringAddress + 4);

			if (stringLength != 0)
				p.Free(firstChar, stringLength);
		}
		#endregion

		#region Injection
		/// <summary>
		/// 
		/// </summary>
		/// <param name="p"></param>
		/// <param name="toStringFuncAddress">The address where our injected toString() is stored</param>
		/// <param name="variableAddress">The address where the object's address will be stored in. After the toString()-execution this address will point to the returned string.</param>
		/// <returns></returns>
		public static void InjectIntoProcess(Process p, out IntPtr toStringFuncAddress, out IntPtr variableAddress, uint variableSize = 8u)
		{
			// Allocate 4 bytes for the variable
			variableAddress = p.Allocate(IntPtr.Zero, variableSize);

			// Build the toString function
			var toStringFunction = BuildToStringCode(variableAddress);

			// Allocate the bytes required for the function
			toStringFuncAddress = p.Allocate(IntPtr.Zero,
				(uint)toStringFunction.Length,
				AllocationType.Commit | AllocationType.Reserve,
				 MemoryProtection.ExecuteReadWrite); // Important: Make it executable

			// Write into the process memory
			p.WriteArray(toStringFuncAddress, toStringFunction);

			// Flush instruction cache
			Imports.FlushInstructionCache(p.GetHandle(), toStringFuncAddress, (uint)toStringFunction.Length);
		}

		static byte[] BuildToStringCode(IntPtr virtualObjectAddress)
		{
			var ms = new MemoryStream();
			var b = new BinaryWriter(ms);

			// 1) Move the object's address into eax.
			// mov eax, DWORD PTR [var]
			ms.WriteByte(0xA1);
			b.Write((uint)virtualObjectAddress.ToInt32());

			// 2) Make a pointer out of eax
			// mov ecx,dword ptr ds:[eax]
			ms.WriteByte(0x8B);
			ms.WriteByte(0x08);

			// 3) Call the object's virtual toString function (or highest re-implementation)
			// call dword ptr ds:[ecx+4]
			ms.WriteByte(0xFF);
			ms.WriteByte(0x51);
			ms.WriteByte(0x04);

			/*
			 * eax contains the string length
			 * edx contains the address of the first char
			 */

			// 4) Store the string + its length
			// mov dword ptr [var], eax
			ms.WriteByte(0xA3);
			b.Write((uint)virtualObjectAddress.ToInt32());

			// Write the edx register into the eax one to be able to save it
			// mov eax,edx
			ms.WriteByte(0x89);
			ms.WriteByte(0xD0);

			// Store the pointer to the first char
			// mov dword ptr [var], eax
			ms.WriteByte(0xA3);
			b.Write((uint)virtualObjectAddress.ToInt32() + 4u);

			// Return
			// ret
			ms.WriteByte(0xc3);

			// Some final spacers
			ms.WriteByte(0xcc); // int3
			ms.WriteByte(0xcc); // int3
			ms.WriteByte(0xcc); // int3

			return ms.GetBuffer();
		}
		#endregion
	}
}
