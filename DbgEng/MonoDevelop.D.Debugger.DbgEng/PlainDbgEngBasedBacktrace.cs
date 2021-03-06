﻿using D_Parser.Dom;
using D_Parser.Parser;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DEW = DebugEngineWrapper;
using MonoDevelop.D.Debugging;
using D_Parser.Resolver;

namespace MonoDevelop.D.DDebugger.DbgEng
{
	class PlainDbgEngBasedBacktrace : IBacktrace, IDBacktraceHelpers
	{
		int fcount;
		protected StackFrame firstFrame;
		public readonly DDebugSession session;
		DissassemblyBuffer[] disBuffers;
		protected int currentFrame = -1;
		public readonly long threadId;
		public readonly DEW.DBGEngine Engine;
		public readonly DLocalExamBacktrace BacktraceHelper;

		public PlainDbgEngBasedBacktrace(DDebugSession session, long threadId, DEW.DBGEngine engine)
		{
			BacktraceHelper = new DLocalExamBacktrace(this);
			this.session = session;
			this.Engine = engine;
			fcount = engine.CallStack.Length;
			this.threadId = threadId;
			if (firstFrame != null)
				this.firstFrame = CreateFrame(Engine.CallStack[0]);
		}

		public int FrameCount
		{
			get
			{
				return fcount;
			}
		}

		public StackFrame[] GetStackFrames(int firstIndex, int lastIndex)
		{
			List<StackFrame> frames = new List<StackFrame>();
			if (firstIndex == 0 && firstFrame != null)
			{
				frames.Add(firstFrame);
				firstIndex++;
			}

			if (lastIndex >= fcount)
				lastIndex = fcount - 1;

			if (firstIndex > lastIndex)
				return frames.ToArray();

			session.SelectThread(threadId);

			for (int n = 0; n < Engine.CallStack.Length; n++)
			{
				frames.Add(CreateFrame(Engine.CallStack[n]));
			}
			return frames.ToArray();
		}

		StackFrame CreateFrame(DEW.StackFrame frameData)
		{

			string fn;
			uint ln;
			ulong off = frameData.InstructionOffset;
			Engine.Symbols.GetLineByOffset(off, out fn, out ln);

			/*
			SourceLocation loc = new SourceLocation (func ?? "?", sfile, line);
			
			long addr;
			if (!string.IsNullOrEmpty (sadr))
				addr = long.Parse (sadr.Substring (2), NumberStyles.HexNumber);
			else
				addr = 0;
			*/

			string methodName = Engine.Symbols.GetNameByOffset(off);

			return new StackFrame((long)off, new SourceLocation(methodName, fn, (int)ln), "Native");

			/*
			string lang = "Native";
			string func = frameData.GetValue ("func");
			string sadr = frameData.GetValue ("addr");
			
			if (func == "??" && session.IsMonoProcess) {
				// Try to get the managed func name
				try {
					ResultData data = session.RunCommand ("-data-evaluate-expression", "mono_pmip(" + sadr + ")");
					string val = data.GetValue ("value");
					if (val != null) {
						int i = val.IndexOf ('"');
						if (i != -1) {
							func = val.Substring (i).Trim ('"',' ');
							lang = "Mono";
						}
					}
				} catch {
				}
			}

			int line = -1;
			string sline = frameData.GetValue ("line");
			if (sline != null)
				line = int.Parse (sline);
			
			string sfile = frameData.GetValue ("fullname");
			if (sfile == null)
				sfile = frameData.GetValue ("file");
			if (sfile == null)
				sfile = frameData.GetValue ("from");
			SourceLocation loc = new SourceLocation (func ?? "?", sfile, line);
			
			long addr;
			if (!string.IsNullOrEmpty (sadr))
				addr = long.Parse (sadr.Substring (2), NumberStyles.HexNumber);
			else
				addr = 0;
			
			return new StackFrame (addr, loc, lang);
			 */
		}

		
		
		public AssemblyLine[] Disassemble(int frameIndex, int firstLine, int count)
		{
			return null;
		}

		public ObjectValue[] GetAllLocals(int frameIndex, EvaluationOptions options)
		{
			return GetLocalVariables(frameIndex, options);
		}

		public ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
		{
			return null;
		}

		public CompletionData GetExpressionCompletionData(int frameIndex, string exp)
		{
			return null;
		}

		public ObjectValue[] GetExpressionValues(int frameIndex, string[] expressions, EvaluationOptions options)
		{
			SelectStackFrame(frameIndex);

			var l = new List<ObjectValue>();

			foreach (var exp in expressions)
				l.Add(BacktraceHelper.CreateObjectValue(exp, options));

			return l.ToArray();
		}

		public class DObjectValue : IDBacktraceSymbol
		{
			readonly PlainDbgEngBasedBacktrace Backtrace;
			WeakReference symbolRef;
			public DEW.DebugScopedSymbol Symbol
			{
				get{ return symbolRef != null ? symbolRef.Target as DEW.DebugScopedSymbol : null; }
				private set { symbolRef = value != null ? new WeakReference(value) : null; }
			}

			public DObjectValue(DEW.DebugScopedSymbol symb, PlainDbgEngBasedBacktrace backtrace)
			{
				this.Backtrace = backtrace;
				Symbol = symb;

				/*var type = symb.TypeName.Replace('@','.');
				if (type.StartsWith("class"))
					type = type.Substring(5);
				if (!string.IsNullOrWhiteSpace(type))
					DType = DParser.ParseBasicType(type);*/
			}

			public ulong Offset
			{
				get { return Symbol.Offset; }
			}

			public string Name
			{
				get { return Symbol.Name; }
			}

			public string TypeName
			{
				get { return typeName ?? (typeName = Symbol.TypeName.Replace('@', '.')); }
			}
			string typeName;

			public AbstractType DType
			{
				get {
					var tn = TypeName;
					if (string.IsNullOrWhiteSpace(tn))
						return null;

					if (tn.StartsWith("class"))
					{
						var td = DParser.ParseBasicType(tn.Substring(5));
						if (td != null)
						{
							Backtrace.BacktraceHelper.TryUpdateStackFrameInfo();
							var t = D_Parser.Resolver.TypeResolution.TypeDeclarationResolver.ResolveSingle(td, Backtrace.BacktraceHelper.ctxt);
							if (t != null)
								return t;
						}
					}

					return null;
				}
			}

			public string Value
			{
				get { return Symbol.TextValue; }
			}

			public string FileName
			{
				get {
					string fileName = "";
					uint line;
					// TODO: Get current debug engine, lookup offset
					return fileName;
				}
			}

			public bool HasParent
			{
				get { return Symbol.ParentId != uint.MaxValue ; }
			}

			DObjectValue parentVal;
			public IDBacktraceSymbol Parent
			{
				get {
					return parentVal ?? (HasParent ? parentVal = new DObjectValue(Symbol.Parent, Backtrace) : null);
				}
			}

			public int ChildCount
			{
				get { return (int)Symbol.ChildrenCount; }
			}

			public IEnumerable<IDBacktraceSymbol> Children
			{
				get {
					foreach (var ch in Symbol.Children)
						yield return new DObjectValue(ch, Backtrace);
				}
			}
		}

		public ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
		{
			SelectStackFrame(frameIndex);
			return BacktraceHelper.GetLocals(options);
		}

		public ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
		{
			SelectStackFrame(frameIndex);
			return BacktraceHelper.GetParameters(options);
		}

		public ObjectValue GetThisReference(int frameIndex, EvaluationOptions options)
		{
			return null;
		}

		public ValidationResult ValidateExpression(int frameIndex, string expression, EvaluationOptions options)
		{
			return new ValidationResult();
		}





		

		public void SelectStackFrame(int frameIndex)
		{
			Engine.CurrentFrameNumber = (uint)frameIndex;
		}

		public void GetCurrentStackFrameInfo(out string file, out ulong offset, out CodeLocation sourceLocation)
		{
			var f = Engine.CurrentFrame;
			offset = f.InstructionOffset;
			uint line;
			Engine.Symbols.GetLineByOffset(offset, out file, out line);
			sourceLocation = new CodeLocation(0, (int)line);
		}

		public IDBacktraceSymbol FindSymbol(string name)
		{
			var symbs = Engine.Symbols.ScopeSymbols;
			if (symbs != null)
				for (var i = symbs.Count - 1; i >= 0; i--)
					if (name == symbs.SymbolName(i))
						return new DObjectValue(symbs.op_Subscript(i), this);
			return null;
		}

		public IEnumerable<IDBacktraceSymbol> Parameters
		{
			get {
				var locals = Engine.Symbols.ScopeParameterSymbols;
				if (locals != null)
					for (uint i = 0; i < locals.Count; i++)
					{
						var s = locals.op_Subscript(i);
						if (s.ParentId == uint.MaxValue) // Root elements
							yield return new DObjectValue(s, this);
					}
			}
		}

		public IEnumerable<IDBacktraceSymbol> Locals
		{
			get {
				var locals = Engine.Symbols.ScopeLocalSymbols;
				if (locals != null)
					for (uint i = 0; i < locals.Count; i++)
					{
						var s = locals.op_Subscript(i);
						if (s.ParentId == uint.MaxValue) // Root elements
							yield return new DObjectValue(s, this);
					}
			}
		}

		public int PointerSize
		{
			get {
				return 4; //TODO
			}
		}

		public byte[] ReadBytes(ulong offset, ulong size)
		{
			return Engine.Memory.ReadVirtual(offset, (uint)size);
		}

		public short ReadInt16(ulong offset)
		{
			return (short)Engine.Memory.ReadVirtualByte(offset);
		}

		public byte ReadByte(ulong offset)
		{
			return Engine.Memory.ReadVirtual(offset, 1)[0];
		}

		public int ReadInt32(ulong offset)
		{
			return (int)Engine.Memory.ReadVirtualInt32(offset);
		}

		public long ReadInt64(ulong offset)
		{
			return (long)Engine.Memory.ReadVirtualInt64(offset);
		}

		public IActiveExamination ActiveExamination
		{
			get { return null; }
		}
	}
}
