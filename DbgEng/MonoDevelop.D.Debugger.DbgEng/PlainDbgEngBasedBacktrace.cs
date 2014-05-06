using D_Parser.Dom;
using D_Parser.Parser;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DEW = DebugEngineWrapper;

namespace MonoDevelop.D.DDebugger.DbgEng
{
	class PlainDbgEngBasedBacktrace : IBacktrace, IObjectValueSource
	{
		int fcount;
		protected StackFrame firstFrame;
		public readonly DDebugSession session;
		DissassemblyBuffer[] disBuffers;
		protected int currentFrame = -1;
		public readonly long threadId;
		public readonly DEW.DBGEngine Engine;

		public PlainDbgEngBasedBacktrace(DDebugSession session, long threadId, DEW.DBGEngine engine)
		{
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
			//StackFrame frm = new StackFrame(
			//Engine.CallStack[0].


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
			Engine.CurrentFrameNumber = (uint)frameIndex;

			var symGroup = Engine.Symbols.ScopeSymbols;
			var l = new List<ObjectValue>();
			foreach (var exp in expressions)
			{
				var path = exp.Split('.');
				if (path.Length < 1)
					l.Add(ObjectValue.CreateUnknown(exp));

				// Get root variable
				DEW.DebugScopedSymbol symb = null;
				var rootVarName = path[0];
				var objPath = new ObjectPath(rootVarName);
				for (uint i = symGroup.Count; i > 0; i--)
				{
					var s = symGroup.op_Subscript(i-1);
					if (s.ParentId == uint.MaxValue && s.Name == rootVarName)
					{
						symb = s;
						break;
					}
				}

				// Get child items
				int k = 1;
				while (k < path.Length && symb != null)
				{
					bool foundSomething = false;
					for (uint i = symGroup.Count; i > 0; i--)
					{
						var s = symGroup.op_Subscript(i - 1);
						if (s.ParentId == symb.Id && s.Name == path[k])
						{
							if(k < path.Length-1)
								objPath = objPath.Append(path[k]);
							symb = s;
							foundSomething = true;
							break;
						}
					}

					if (!foundSomething)
						symb = null;
					k++;
				}

				if (symb == null)
					l.Add(ObjectValue.CreateUnknown(exp));
				else
					l.Add(CreateObjectValue(symb, path.Length == 1 ? new ObjectPath() : objPath));
			}

			return l.ToArray();
		}

		public class DObjectValue
		{
			public ITypeDeclaration DType;
			WeakReference symbolRef;
			public DEW.DebugScopedSymbol Symbol
			{
				get{ return symbolRef != null ? symbolRef.Target as DEW.DebugScopedSymbol : null; }
				private set { symbolRef = value != null ? new WeakReference(value) : null; }
			}

			public DObjectValue(DEW.DebugScopedSymbol symb)
			{
				Symbol = symb;

				var type = symb.TypeName.Replace('@','.');
				if (type.StartsWith("class"))
					type = type.Substring(5);
				if (!string.IsNullOrWhiteSpace(type))
					DType = DParser.ParseBasicType(type);
			}
		}

		protected readonly Dictionary<string, DObjectValue> ValueCache = new Dictionary<string, DObjectValue>();

		protected static string BuildObjPath(ObjectPath p, string name = null)
		{
			return (p.Length > 0 ? p.Join(".") : "") + (string.IsNullOrWhiteSpace(name) ? "" : ("." + name));
		}

		protected virtual ObjectValue CreateObjectValue(DEW.DebugScopedSymbol symb, ObjectPath parentPath)
		{
			var name = symb.Name;
			var childPath = parentPath.Length == 0 ? new ObjectPath(name) : parentPath.Append(name);

			if (string.IsNullOrWhiteSpace(name))
				return ObjectValue.CreateUnknown(null, childPath, symb.TypeName);

			var childCount = (int)symb.ChildrenCount;

			
			var v = new DObjectValue(symb);

			ValueCache[BuildObjPath(parentPath, name)] = v;
			
			ObjectValue ov = null;

			if (childCount > 0)
			{
				ov = ObjectValue.CreateArray(this, childPath, symb.TypeName, childCount, ObjectValueFlags.Field, null);
			}
			else if (v.DType is ArrayDecl)
			{
				uint sz;
				if (symb.Size == 8)
					sz = Engine.Memory.ReadVirtualInt32(symb.Offset);
				else if (symb.Size == 16)
					sz = (uint)Engine.Memory.ReadVirtualInt64(symb.Offset);
				else
					sz = 0;
				
				if(ov == null)
					ov = ObjectValue.CreateArray(this, childPath, v.DType.ToString(), (int)sz, ObjectValueFlags.Array, null);
			}
			else
			{
				ov = ObjectValue.CreatePrimitive(this, childPath, symb.TypeName, new EvaluationResult(symb.TextValue), ObjectValueFlags.Field);
			}
			return ov;
		}

		public ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
		{
			var values = new List<ObjectValue>();
			Engine.CurrentFrameNumber = (uint)frameIndex;
			var locals = Engine.Symbols.ScopeLocalSymbols;
			if (locals != null)
				for (uint i = 0; i < locals.Count; i++)
				{
					var s = locals.op_Subscript(i);
					if(s.ParentId == uint.MaxValue) // Root elements
						values.Add(CreateObjectValue(s, new ObjectPath()));
				}

			return values.ToArray();
		}

		public ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
		{
			var values = new List<ObjectValue>();
			Engine.CurrentFrameNumber = (uint)frameIndex;
			var locals = Engine.Symbols.ScopeParameterSymbols;
			if (locals != null)
				for (uint i = 0; i < locals.Count; i++)
				{
					var s = locals.op_Subscript(i);
					if (s.ParentId == uint.MaxValue) // Root elements
						values.Add(CreateObjectValue(s, new ObjectPath()));
				}

			return values.ToArray();
		}

		public ObjectValue GetThisReference(int frameIndex, EvaluationOptions options)
		{
			return null;
		}

		public ValidationResult ValidateExpression(int frameIndex, string expression, EvaluationOptions options)
		{
			return new ValidationResult();
		}





		public virtual ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
		{
			DObjectValue v;
			if (!ValueCache.TryGetValue(BuildObjPath(path), out v))
				return null;
			
			var s = v.Symbol;

			var ch = new List<ObjectValue>();
			var children = s.Children;
			for (int c = count; c > 0; c++)
			{
				ch.Add(CreateObjectValue(children[index], path));
				index++;
			}

			return ch.ToArray();
		}

		public virtual object GetRawValue(ObjectPath path, EvaluationOptions options)
		{
			return null;
		}

		public virtual ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
		{
			return null;
		}

		public virtual void SetRawValue(ObjectPath path, object value, EvaluationOptions options)
		{
		}

		public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
		{
			return null;
		}
	}
}
