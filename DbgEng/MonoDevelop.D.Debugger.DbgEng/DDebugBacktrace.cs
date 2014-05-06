
using System;
using System.Collections.Generic;
using System.Globalization;
using Mono.Debugging.Client;
using Mono.Debugging.Backend;
using MonoDevelop.Ide;

using DEW = DebugEngineWrapper;

namespace MonoDevelop.D.DDebugger.DbgEng
{
	class DDebugBacktrace : PlainDbgEngBasedBacktrace
	{
		MonoDSymbolResolver symbolResolver;

		public DDebugBacktrace(DDebugSession session, long threadId, DEW.DBGEngine engine)
			: base(session, threadId, engine)
		{
			symbolResolver = new MonoDSymbolResolver(this, engine);
		}

		public ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
		{
			List<ObjectValue> values = new List<ObjectValue>();
			if (Engine.Symbols.ScopeLocalSymbols == null)
				return values.ToArray();

			for (uint i = 0; i < Engine.Symbols.ScopeLocalSymbols.Count; i++)
			{
				if (Engine.Symbols.ScopeLocalSymbols.Symbols[i].Parent != null)
					continue;

				string name = Engine.Symbols.ScopeLocalSymbols.Symbols[i].Name;
				string typename = Engine.Symbols.ScopeLocalSymbols.Symbols[i].TypeName;
				string val = Engine.Symbols.ScopeLocalSymbols.Symbols[i].TextValue;
				ulong offset = Engine.Symbols.ScopeLocalSymbols.Symbols[i].Offset;
				DEW.DebugScopedSymbol parentSymbol = Engine.Symbols.ScopeLocalSymbols.Symbols[i].Parent;

				ObjectValue ov = symbolResolver.Resolve(offset, name, typename, val, parentSymbol);
				if (ov == null)
				{
					ObjectValueFlags flags = ObjectValueFlags.Variable;
					ov = ObjectValue.CreatePrimitive(this, new ObjectPath(name), typename, new EvaluationResult(val), flags);
				}

				if (ov != null)
					values.Add(ov);
			}
			return values.ToArray();
		}

		public ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
		{
			List<ObjectValue> values = new List<ObjectValue>();

			SelectFrame(frameIndex);

			return values.ToArray();
		}

		public ObjectValue GetThisReference(int frameIndex, EvaluationOptions options)
		{
			return null;
		}

		public ObjectValue[] GetAllLocals(int frameIndex, EvaluationOptions options)
		{
			List<ObjectValue> locals = new List<ObjectValue>();
			/*
			locals.AddRange (GetParameters (frameIndex, options));
			*/
			locals.AddRange(GetLocalVariables(frameIndex, options));
			return locals.ToArray();
		}

		public ObjectValue[] GetExpressionValues(int frameIndex, string[] expressions, EvaluationOptions options)
		{
			List<ObjectValue> values = new List<ObjectValue>();

			SelectFrame(frameIndex);
			foreach (string exp in expressions)
				values.Add(CreateVarObject(exp));
			return values.ToArray();
		}

		public ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
		{
			return null;
		}

		public ValidationResult ValidateExpression(int frameIndex, string expression, EvaluationOptions options)
		{
			return new ValidationResult(true, null);
		}

		public CompletionData GetExpressionCompletionData(int frameIndex, string exp)
		{
			SelectFrame(frameIndex);

			return null;
		}

		DEW.DebugScopedSymbol FindScopedSymbol(string symbolname, DEW.DebugScopedSymbol[] symbols)
		{
			List<string> path = new List<string>(symbolname.Split('.'));

			for (uint i = 0; i < symbols.Length; i++)
			{
				if (path[0] == symbols[i].Name)
				{
					path.RemoveAt(0);
					if (path.Count > 0)
						return FindScopedSymbol(string.Join(".", path.ToArray()), symbols[i].Children);
					else
						return symbols[i];
				}
			}
			return null;
		}

		ObjectValue CreateVarObject(string exp)
		{
			try
			{
				session.SelectThread(threadId);

				//DebugEngineWrapper.DebugSymbolData[] datasymbols = Engine.Symbols.GetSymbols("*");

				var rootSymbols = Array.FindAll<DEW.DebugScopedSymbol>(Engine.Symbols.ScopeLocalSymbols.Symbols, (a) => (a.Parent == null));
				DEW.DebugScopedSymbol foundSymbol = FindScopedSymbol(exp, rootSymbols);

				if (foundSymbol != null)
				{
					session.RegisterTempVariableObject(exp);
					return CreateObjectValue(exp, foundSymbol);
				}
				return ObjectValue.CreateUnknown(exp);
			}
			catch
			{
				return ObjectValue.CreateUnknown(exp);
			}
		}

		ObjectValue CreateObjectValue(string name, DebugEngineWrapper.DebugScopedSymbol symbol)
		{

			string vname = symbol.Name;
			string typeName = symbol.TypeName;
			string value = symbol.TextValue;
			int nchild = (int)symbol.ChildrenCount;

			ObjectValue val = symbolResolver.Resolve(symbol.Offset, vname, typeName, value, symbol.Parent);
			if (val == null)
			{
				ObjectValueFlags flags = ObjectValueFlags.Variable;

				// There can be 'public' et al children for C++ structures
				if (typeName == null)
					typeName = "none";

				val = ObjectValue.CreatePrimitive(this, new ObjectPath(vname), typeName, new EvaluationResult(value), flags);
				val.Name = name;
			}

			return val;
		}

		public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
		{
			List<ObjectValue> children = new List<ObjectValue>();
			session.SelectThread(threadId);

			if (Engine.Symbols.ScopeLocalSymbols == null)
				return children.ToArray();

			DEW.DebugScopedSymbol parent = null;

			for (uint i = 0; i < Engine.Symbols.ScopeLocalSymbols.Symbols.Length; i++)
			{
				DEW.DebugScopedSymbol symbol = Engine.Symbols.ScopeLocalSymbols.Symbols[i];
				if (symbol.Name == path.LastName)
				{
					parent = symbol;
					break;
				}
			}

			if (parent == null || parent.ChildrenCount == 0)
				return children.ToArray();

			for (uint i = 0; i < parent.ChildrenCount; i++)
			{

				DEW.DebugScopedSymbol child = parent.Children[i];

				string name = child.Name;
				string typename = child.TypeName;
				string val = child.TextValue;
				ulong offset = child.Offset;

				ObjectValue ov = symbolResolver.Resolve(offset, name, typename, val, child.Parent);
				if (ov == null)
				{
					ObjectValueFlags flags = ObjectValueFlags.Variable;
					ov = ObjectValue.CreatePrimitive(this, new ObjectPath(name), typename, new EvaluationResult(val), flags);
				}

				if (ov != null)
					children.Add(ov);
			}

			return children.ToArray();
		}

		public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
		{
			session.SelectThread(threadId);

			return new EvaluationResult(value);
		}

		public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
		{
			throw new NotSupportedException();
		}

		void SelectFrame(int frame)
		{
			session.SelectThread(threadId);

			/*
			if (frame != currentFrame) {
				session.RunCommand ("-stack-select-frame", frame.ToString ());
				currentFrame = frame;
			}
			 */
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
			SourceLocation loc = new SourceLocation(methodName, fn, (int)ln);

			return new StackFrame((long)off, loc, "Native");

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
			SelectFrame(frameIndex);
			return null;
		}

		public object GetRawValue(ObjectPath path, EvaluationOptions options)
		{
			return null;
		}

		public void SetRawValue(ObjectPath path, object value, EvaluationOptions options)
		{
		}
	}

}
