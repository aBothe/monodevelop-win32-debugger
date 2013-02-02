using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Extemory;
using Extemory.Injection;
using Extemory.Processes;
using System.Diagnostics;
using System.IO;
using Extemory.Win32;
using System.Runtime.InteropServices;

namespace DHijack
{
	public partial class Form1 : Form
	{
		public Form1()
		{
			InitializeComponent();
		}

		Process p;
		IntPtr toStringFuncAddress;
		IntPtr varAddress;
		bool isInjected = false;

		private void ExecuteToString(object sender, EventArgs e)
		{
			if (p == null || !isInjected)
			{
				MessageBox.Show("Inject first!");
				return;
			}

			uint addr = 0u;

			if (!UInt32.TryParse(input_objAddress.Text, out addr))
			{
				MessageBox.Show("Enter valid object address!");
				return;
			}

			var sw = new Stopwatch();
			sw.Restart();
			// Store the object address into the allocated space
			Inject.FreeDString(p, varAddress);

			p.Write(varAddress, addr);
			p.Write(varAddress + 4, 0u);

			Inject.ExecuteFunction(p, toStringFuncAddress);

			objString.Text = Inject.ReadDString(p, varAddress);
			sw.Stop();
			Text = sw.Elapsed.TotalMilliseconds + "ms";
		}

		private void InjectIntoProcess(object sender, EventArgs e)
		{
			try
			{
				int pid = 0;
				if (Int32.TryParse(this.pid.Text, out pid))
					p = Process.GetProcessById(pid);
				else
					p = Process.GetProcessesByName(this.pid.Text).First();
			}
			catch
			{
				MessageBox.Show("Invalid process id/name!");
				return;
			}

			if (p == null)
				return;

			Inject.InjectIntoProcess(p, out toStringFuncAddress, out varAddress, 8u);
			isInjected = true;
		}
	}
}
