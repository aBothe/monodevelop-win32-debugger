
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Mono.Debugging.Client;
using Mono.Debugging.Backend;
using MonoDevelop.Core.Execution;
using MonoDevelop.Debugger;

namespace MonoDevelop.D.DDebugger.DbgEng
{
	public class DDebugSessionFactory : IDebuggerEngine
	{
		struct FileData
		{
			public DateTime LastCheck;
			public bool IsExe;
		}

		Dictionary<string, FileData> fileCheckCache = new Dictionary<string, FileData>();

		public bool CanDebugCommand(ExecutionCommand command)
		{
			NativeExecutionCommand cmd = command as NativeExecutionCommand;
			if (cmd == null)
				return false;

			string file = FindFile(cmd.Command);
			if (!File.Exists(file))
			{
				// The provided file is not guaranteed to exist. If it doesn't
				// we assume we can execute it because otherwise the run command
				// in the IDE will be disabled, and that's not good because that
				// command will build the project if the exec doesn't yet exist.
				return true;
			}

			file = Path.GetFullPath(file);
			DateTime currentTime = File.GetLastWriteTime(file);

			FileData data;
			if (fileCheckCache.TryGetValue(file, out data))
			{
				if (data.LastCheck == currentTime)
					return data.IsExe;
			}
			data.LastCheck = currentTime;
			try
			{
				data.IsExe = IsExecutable(file);
			}
			catch (IOException ex)
			{
				// The file could still be in use by compiler, so don't want to report that the file is not an exe
				return false;
			}
			catch
			{
				data.IsExe = false;
			}
			fileCheckCache[file] = data;
			return data.IsExe;
		}

		public DebuggerStartInfo CreateDebuggerStartInfo(ExecutionCommand command)
		{
			var pec = (NativeExecutionCommand)command;
			var startInfo = new DebuggerStartInfo();

			var cmd = pec.Command;
			RunCv2Pdb(ref cmd);

			startInfo.Command = cmd;
			startInfo.Arguments = pec.Arguments;
			startInfo.WorkingDirectory = pec.WorkingDirectory;
			if (pec.EnvironmentVariables.Count > 0)
			{
				foreach (KeyValuePair<string, string> val in pec.EnvironmentVariables)
					startInfo.EnvironmentVariables[val.Key] = val.Value;
			}
			return startInfo;
		}

		public static void RunCv2Pdb(ref string target)
		{
			const string cv2pdb = "cv2pdb.exe";

			var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
			var p = Path.Combine(dir, cv2pdb);
			if (!File.Exists(p))
			{
				p = cv2pdb;
				dir = null;
			}

			var pdb = Path.ChangeExtension(target, ".pdb");

			var psi = new ProcessStartInfo(p, "\"" + target + "\" \""+ (target = Path.ChangeExtension(target,".pdb.exe")) +"\" \"" + pdb + "\"");
			psi.UseShellExecute = false;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;
			psi.CreateNoWindow = true;
			if (dir != null)
				psi.WorkingDirectory = dir;

			Process proc;
			try
			{
				proc = Process.Start(psi);
			}
			catch (Exception ex)
			{
				throw new Exception("Error running cv2pdb.exe on target: " + target +
									"\r\nPlease ensure that path to cv2pdb is registered in the 'PATH' environment variable" +
									"\r\nDetails:\r\n" + ex.Message);
			}

			if (!proc.WaitForExit(30000))
				proc.Kill();
			/*
			if (proc.ExitCode != 0)
				throw new Exception("Couldn't execute cv2pdb: " + proc.StandardError.ReadToEnd() + "\r\n" + proc.StandardOutput.ReadToEnd());
			*/
			if (!File.Exists(pdb) || !File.Exists(target))
				throw new FileNotFoundException("Error during cv2pdb execution", pdb, new Exception(proc.StandardError.ReadToEnd() + "\r\n" + proc.StandardOutput.ReadToEnd()));
		}

		public bool IsExecutable(string file)
		{
			// HACK: this is a quick but not very reliable way of checking if a file
			// is a native executable. Actually, we are interested in checking that
			// the file is not a script.
			using (StreamReader sr = new StreamReader(file))
			{
				char[] chars = new char[3];
				int n = 0, nr = 0;
				while (n < chars.Length && (nr = sr.ReadBlock(chars, n, chars.Length - n)) != 0)
					n += nr;
				if (nr != chars.Length)
					return true;
				if (chars[0] == '#' && chars[1] == '!')
					return false;
			}
			return true;
		}

		public DebuggerSession CreateSession()
		{
			DDebugSession ds = new DDebugSession();
			return ds;
		}

		public ProcessInfo[] GetAttachableProcesses()
		{
			Process[] processlist = Process.GetProcesses();

			List<ProcessInfo> procs = new List<ProcessInfo>();
			foreach (Process process in processlist)
			{

				ProcessInfo pi = new ProcessInfo(process.Id, process.ProcessName);
				procs.Add(pi);
			}
			return procs.ToArray();
		}

		string FindFile(string cmd)
		{
			if (Path.IsPathRooted(cmd))
				return cmd;
			string pathVar = Environment.GetEnvironmentVariable("PATH");
			string[] paths = pathVar.Split(Path.PathSeparator);
			foreach (string path in paths)
			{
				string file = Path.Combine(path, cmd);
				if (File.Exists(file))
					return file;
			}
			return cmd;
		}
	}
}
