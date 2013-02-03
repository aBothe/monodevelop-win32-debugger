
using System;
using System.Globalization;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Mono.Debugging.Client;
using MonoDevelop.Core;
using MonoDevelop.Core.Execution;
//using Mono.Unix.Native;
using MagoWrapper;

namespace MonoDevelop.D.DDebugger.Mago
{
    class BreakPointWrapper
    {
        public BreakEventInfo EventInfo { get; set; }
        public ulong Key { get; set; }
        public BreakPointWrapper(ulong key, BreakEventInfo eventInfo)
        {
            Key = key;
            EventInfo = eventInfo;
        }
    }

    class DDebugSession : DebuggerSession
    {
        MagoWrapper.DDebugger engine = null;
        MagoWrapper.Debuggee debuggee;
        MagoWrapper.SymbolResolver symbolResolver;

        bool IsDebugging;
        bool EngineStarting;
        bool StopWaitingForEvents = false;

        IProcessAsyncOperation console;

        long currentThread = -1;
        uint activeThread = 0;

        long targetProcessId = 0;
        List<string> tempVariableObjects = new List<string>();
        Dictionary<ulong, BreakPointWrapper> breakpoints = new Dictionary<ulong, BreakPointWrapper>();
        List<BreakEventInfo> breakpointsWithHitCount = new List<BreakEventInfo>();

        DateTime lastBreakEventUpdate = DateTime.Now;
        Dictionary<int, WaitCallback> breakUpdates = new Dictionary<int, WaitCallback>();
        bool breakUpdateEventsQueued;
        const int BreakEventUpdateNotifyDelay = 500;

        bool logGdb;

        object syncLock = new object();
        object eventLock = new object();

        uint breakpointcounter = 0;
        ulong lastLineAddress;

        ManualResetEvent debuggeeEvent;

        public DDebugSession()
        {
            logGdb = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MONODEVELOP_GDB_LOG"));

        }

        public MagoWrapper.DDebugger Engine
        {
            get
            {
                if (engine == null)
                {
                    engine = new MagoWrapper.DDebugger();
                }

                return engine;
            }
        }

        public MagoWrapper.SymbolResolver SymbolResolver
        {
            get
            {
                if (symbolResolver == null)
                {
                    symbolResolver = Engine.CreateSymbolResolver(this.debuggee);
                }
                return symbolResolver;
            }
        }

        private void DestroyEngine()
        {
            if (symbolResolver != null)
            {
                symbolResolver.Dispose();
                symbolResolver = null;
            }

            if (debuggee != null)
            {
                this.debuggee.Dispose();
                debuggee = null;
            }
        }

        private void RegisterCallbacks()
        {
            debuggee.OnProcessStart += delegate()
            {
                breakpointcounter = 0;

                Console.WriteLine("on process started");
            };

            debuggee.OnProcessExit += delegate(uint exitCode)
            {
                IsDebugging = false;

                debuggeeEvent.Set();

                ThreadPool.QueueUserWorkItem(delegate(object data)
                {
                    try
                    {
                        TargetEventArgs args = new TargetEventArgs(TargetEventType.TargetExited);
                        OnTargetEvent(args);
                        DestroyEngine();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }, null);


                Console.WriteLine("on process exited");
            };

            debuggee.OnThreadExit += delegate(uint threadId, uint exitCode)
            {
                Console.WriteLine("on thread exit");
            };

            debuggee.OnOutputString += delegate(string outputString)
            {
                Console.WriteLine("on output string: " + outputString);
            };


            debuggee.OnLoadComplete += delegate(uint threadId)
            {
                debuggeeEvent.Set();
                Console.WriteLine("on loadcomplete");
            };

            debuggee.OnException += delegate(uint threadId, bool firstChance, ExceptionRecord exceptRec)
            {
                debuggeeEvent.Set();
                Console.WriteLine("on exception");

                //StopWaitingForEvents = true;
                //this.HandleException(new Exception(ex.Message));

                return true;
            };

            debuggee.OnBreakpoint += delegate(uint threadId, uint address)
            {
                debuggeeEvent.Set();
                Console.WriteLine("on breakpoint");

                activeThread = (uint)threadId;
                lastLineAddress = address;

                FireBreakPoint(lastLineAddress);
                StopWaitingForEvents = true;

                return true;
            };

            debuggee.OnStepComplete += delegate(uint threadId)
            {
                debuggeeEvent.Set();
                Console.WriteLine("on step complete");

                lastLineAddress = this.debuggee.GetCurrentInstructionAddress();

                FireBreakPoint(lastLineAddress);
                StopWaitingForEvents = true;
            };
        }

        protected override void OnRun(DebuggerStartInfo startInfo)
        {
            targetProcessId = 0;

            StartDebuggerSession(startInfo, 0);

            targetProcessId = debuggee.GetProcessId();

            OnStarted();

            debuggee.Continue();

        }

        protected override void OnAttachToProcess(long processId)
        {
            //ToDo: fix/implement attaching to process
            targetProcessId = processId;

            StartDebuggerSession(null, processId);
            IsDebugging = true;
        }

        void StartDebuggerSession(DebuggerStartInfo startInfo, long attachToProcessId)
        {
            IsDebugging = true;
            EngineStarting = true;

            debuggeeEvent = new ManualResetEvent(false);
            debuggee = new Debuggee();
            RegisterCallbacks();

            Engine.LaunchExecutable(startInfo.Command + (string.IsNullOrWhiteSpace(startInfo.Arguments) ? "" : (" " + startInfo.Arguments)), Path.GetDirectoryName(startInfo.Command), debuggee);

            WaitForDebugEvent();

            EngineStarting = false;
        }

        public void GotoCurrentLocation()
        {
            if (!IsDebugging || StopWaitingForEvents) return;

            StopWaitingForEvents = true;

        }


        void WaitForDebugEvent()
        {
            if (!IsDebugging) return;

            debuggeeEvent.WaitOne();

        }

        void InternalStop()
        {
            if (IsDebugging)
            {
                IsDebugging = false;

                debuggee.Terminate();
            }
        }

        public override void Dispose()
        {
            if (console != null && !console.IsCompleted)
            {
                console.Cancel();
                console = null;
            }

        }

        protected override void OnSetActiveThread(long processId, long threadId)
        {
            activeThread = (uint)threadId;
        }

        protected override void OnStop()
        {
            InternalStop();
        }

        protected override void OnDetach()
        {
            TargetEventArgs args = new TargetEventArgs(TargetEventType.TargetExited);
            OnTargetEvent(args);
        }

        protected override void OnExit()
        {
            InternalStop();

            TargetEventArgs args = new TargetEventArgs(TargetEventType.TargetExited);
            OnTargetEvent(args);

        }

        protected override void OnStepLine()//step into
        {
            if (!IsDebugging) return;

            debuggee.StepIn();
            WaitForDebugEvent();
            StopWaitingForEvents = false;
            GotoCurrentLocation();
        }

        protected override void OnNextLine()//step over
        {
            if (!IsDebugging) return;

            debuggee.StepOver();
            WaitForDebugEvent();
            StopWaitingForEvents = false;
            GotoCurrentLocation();
        }

        protected override void OnFinish() //step out
        {
            if (!IsDebugging) return;

            debuggee.StepOut();
            WaitForDebugEvent();
            StopWaitingForEvents = false;
            GotoCurrentLocation();

        }


        protected override void OnStepInstruction() //what is this for?
        {
            if (!IsDebugging) return;
            WaitForDebugEvent();
            StopWaitingForEvents = false;
            GotoCurrentLocation();
        }

        protected override void OnNextInstruction()  //what is this for?
        {
            if (!IsDebugging) return;
            WaitForDebugEvent();
            StopWaitingForEvents = false;
            GotoCurrentLocation();
        }

        void FireBreakPoint(ulong address)
        {
            TargetEventArgs args = new TargetEventArgs(TargetEventType.TargetHitBreakpoint);

            ulong tempAddress = address;
            if (breakpoints.ContainsKey(tempAddress))
            {
                //breakpoints[(ulong)tempoff].EventInfo.UpdateHitCount((int)breakpoints[(ulong)tempoff].Breakpoint.HitCount);
                args.BreakEvent = breakpoints[tempAddress].EventInfo.BreakEvent;
            }
            else
            {
                args = new TargetEventArgs(TargetEventType.TargetStopped);
                BreakEventInfo breakInfo = new BreakEventInfo();
                breakInfo.Handle = tempAddress;
                breakInfo.SetStatus(BreakEventStatus.Bound, null);
                string filename;
                uint line;
                if (this.SymbolResolver.GetCodeLineFromAddress(address, out filename, out line))
                {
                    args.BreakEvent = breakInfo.BreakEvent;
                }
            }

            ProcessInfo process = OnGetProcesses()[0];
            args.Process = new ProcessInfo(process.Id, process.Name);

            args.Backtrace = new Backtrace(new DDebugBacktrace(this, activeThread, this.debuggee));//, Engine));

            ThreadPool.QueueUserWorkItem(delegate(object data)
            {
                try
                {
                    OnTargetEvent((TargetEventArgs)data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }, args);
        }



        protected override BreakEventInfo OnInsertBreakEvent(BreakEvent be)
        {
            Breakpoint bp = be as Breakpoint;
            if (bp == null)
                throw new NotSupportedException();

            BreakEventInfo breakEventInfo = new BreakEventInfo();

            if (bp.HitCount > 0)
            {
                breakpointsWithHitCount.Add(breakEventInfo);
            }

            ulong address = this.SymbolResolver.GetAddressFromCodeLine(bp.FileName, (ushort)bp.Line);
            if (address != 0)
            {
                breakpointcounter++;
                debuggee.SetBreakPoint(address, breakpointcounter);
                breakpoints[address] = new BreakPointWrapper(breakpointcounter, breakEventInfo);
                breakEventInfo.Handle = address;
                breakEventInfo.SetStatus(BreakEventStatus.Bound, null);
            }
            else
            {
                breakEventInfo.SetStatus(BreakEventStatus.BindError, null);
            }

            return breakEventInfo;

        }

        protected override void OnRemoveBreakEvent(BreakEventInfo binfo)
        {

            if (binfo == null)
                return;

            breakpointsWithHitCount.Remove(binfo);
            if (breakpoints.ContainsKey((ulong)binfo.Handle))
            {
                ulong key = breakpoints[(ulong)binfo.Handle].Key;
                breakpoints.Remove((ulong)binfo.Handle);
                debuggee.RemoveBreakPoint((ulong)binfo.Handle, key);
            }
        }

        protected override void OnEnableBreakEvent(BreakEventInfo binfo, bool enable)
        {
            if (binfo.Handle == null)
                return;

            //breakpoints[(ulong)binfo.Handle].Breakpoint.Flags =  enable? BreakPointOptions.Enabled : BreakPointOptions.Deferred;

            //ToDo: tell engine we enabled a break point
        }

        protected override void OnUpdateBreakEvent(BreakEventInfo binfo)
        {
            return;

        }

        protected override void OnContinue()
        {
            if (!IsDebugging) return;

            debuggee.Continue();
            WaitForDebugEvent();
        }

        protected override ThreadInfo[] OnGetThreads(long processId)
        {
            Process process = Process.GetProcessById((int)processId);
            List<ThreadInfo> list = new List<ThreadInfo>();

            foreach (ProcessThread thread in process.Threads)
            {
                list.Add(new ThreadInfo(processId, thread.Id, "Thread #" + thread.Id.ToString(), ""));
            }
            return list.ToArray();
        }

        protected override ProcessInfo[] OnGetProcesses()
        {
            Process process = Process.GetProcessById((int)targetProcessId);
            return new ProcessInfo[] { new ProcessInfo(process.Id, process.ProcessName) };
        }

        ThreadInfo GetThread(long id)
        {
            return new ThreadInfo(0, id, "Thread #" + id, null);
        }

        protected override Backtrace OnGetThreadBacktrace(long processId, long threadId)
        {
            return new Backtrace(new DDebugBacktrace(this, (uint)threadId, this.debuggee));
        }

        protected override AssemblyLine[] OnDisassembleFile(string file)
        {
            return null;
        }

        public void SelectThread(long id)
        {
            if (id == currentThread)
                return;
            currentThread = id;
            //ToDo: select thread on engine wrapper
        }

        string Escape(string str)
        {
            if (str == null)
                return null;
            else if (str.IndexOf(' ') != -1 || str.IndexOf('"') != -1)
            {
                str = str.Replace("\"", "\\\"");
                return "\"" + str + "\"";
            }
            else
                return str;
        }


        internal void RegisterTempVariableObject(string var)
        {
            tempVariableObjects.Add(var);
        }

        void CleanTempVariableObjects()
        {
            //ToDo: remove temp variables
            tempVariableObjects.Clear();
        }
    }
}
