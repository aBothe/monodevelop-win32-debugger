
using System;
using System.Collections.Generic;
using System.Globalization;
using Mono.Debugging.Client;
using Mono.Debugging.Backend;
using MonoDevelop.Ide;

using DEW = DebugEngineWrapper;
using D_Parser.Dom;
using D_Parser.Dom.Statements;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver;
using D_Parser.Parser;
using D_Parser.Misc;
using MonoDevelop.D;

namespace MonoDevelop.D.DDebugger.DbgEng
{


    class DDebugBacktrace : IBacktrace, IObjectValueSource
    {
        int fcount;
        StackFrame firstFrame;
        DDebugSession session;
        DissassemblyBuffer[] disBuffers;
        int currentFrame = -1;
        long threadId;
        DEW.DBGEngine Engine;

        public DDebugBacktrace(DDebugSession session, long threadId, DEW.DBGEngine engine)
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
            //DDebugCommandResult res = session.RunCommand("-stack-list-frames", firstIndex.ToString(), lastIndex.ToString());
            //ResultData stack = res.GetObject ("stack");

            for (int n = 0; n < Engine.CallStack.Length; n++)
            {
                //ResultData frd = stack.GetObject (n);
                frames.Add(CreateFrame(Engine.CallStack[n]));
            }
            return frames.ToArray();
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

                ObjectValue ov = FindParsedDVariable(offset, name, typename, val, parentSymbol);
                if (ov == null)
                {
                    ObjectValueFlags flags = ObjectValueFlags.Variable;
                    ov = ObjectValue.CreatePrimitive(this, new ObjectPath(name), typename, new EvaluationResult(val), flags);
                }

                if (ov != null)
                    values.Add(ov);
            }
            return values.ToArray();

            //Engine.CallStack[0]
            //Engine
            //ScopeLocalSymbols

            /*
            List<ObjectValue> values = new List<ObjectValue> ();
            SelectFrame (frameIndex);
            DDebugCommandResult res = session.RunCommand("-stack-list-locals", "0");
            foreach (ResultData data in res.GetObject ("locals"))
                values.Add (CreateVarObject (data.GetValue ("name")));
			
            return values.ToArray ();
            */


        }

        public static IAbstractSyntaxTree GetFileSyntaxTree(string file, out DProject OwnerProject)
        {
            OwnerProject = null;
            var proj = IdeApp.Workbench.ActiveDocument.Project as DProject;
            if (proj != null)
            {
                if (proj != null && (proj.Files.GetFile(file) != null))
                {
                    OwnerProject = proj;
                    var modules = proj.LocalFileCache as IEnumerable<IAbstractSyntaxTree>;
                    foreach (IAbstractSyntaxTree module in modules)
                    {
                        if (module.FileName.Equals(file, StringComparison.InvariantCultureIgnoreCase))
                            return module;
                    }
                }
            }
            return null;
        }

        private AbstractType[] ResolveParentSymbol(DEW.DebugScopedSymbol parentsymbol, ResolverContextStack ctxt)
        {
            if (parentsymbol.Parent != null)
            {
                return TypeDeclarationResolver.ResolveFurtherTypeIdentifier(parentsymbol.Name, ResolveParentSymbol(parentsymbol.Parent, ctxt), ctxt, null);
            }
            else
            {
                return TypeDeclarationResolver.ResolveIdentifier(parentsymbol.Name, ctxt, null);
            }

        }

        private ObjectValue FindParsedDVariable(ulong offset, string symbolname, string typename, string val, DEW.DebugScopedSymbol parentsymbol)
        {
            IAbstractSyntaxTree module;
            int codeLine;
            INode variableNode = null;

            // Search currently scoped module
            string file = "";
            uint line = 0;
            Engine.Symbols.GetLineByOffset(Engine.CurrentInstructionOffset, out file, out line);
            codeLine = (int)line;

            if (string.IsNullOrWhiteSpace(file))
                return null;

            DProject dproj = null;
            module = GetFileSyntaxTree(file, out dproj);

            // If syntax tree built, search the variable location
            if (module != null)
            {
                IStatement stmt = null;

                var block = DResolver.SearchBlockAt(module, new CodeLocation(0, codeLine), out stmt);

                var ctxt = new ResolverContextStack(dproj.ParseCache,
                        new ResolverContext { ScopedBlock = block, ScopedStatement = stmt });

                AbstractType[] res;
                if (parentsymbol != null)
                {
                    var parentres = ResolveParentSymbol(parentsymbol, ctxt);
                    res = TypeDeclarationResolver.ResolveFurtherTypeIdentifier(symbolname, parentres, ctxt, null);
                }
                else
                {
                    res = TypeDeclarationResolver.ResolveIdentifier(symbolname, ctxt, null);
                }

                if (res != null && res.Length > 0 && res[0] is DSymbol)
                {
                    variableNode = (res[0] as DSymbol).Definition;
                }
            }

            // Set type string
            string _typeString = typename;
            if (variableNode != null)
            {
                var t = variableNode.Type;
                if (t != null)
                    _typeString = t.ToString();
            }

            // Set value string
            string _valueString = val;

            ObjectValueFlags flags = ObjectValueFlags.Variable;

            if (variableNode != null)
            {
                ITypeDeclaration curValueType = variableNode.Type;
                if (curValueType != null)
                {
                    if (!IsBasicType(curValueType))
                    {
                        if (_typeString == "string") //TODO: Replace this by searching the alias definition in the cache
                            curValueType = new ArrayDecl() { InnerDeclaration = new DTokenDeclaration(DTokens.Char) };
                        else if (_typeString == "wstring")
                            curValueType = new ArrayDecl() { InnerDeclaration = new DTokenDeclaration(DTokens.Wchar) };
                        else if (_typeString == "dstring")
                            curValueType = new ArrayDecl() { InnerDeclaration = new DTokenDeclaration(DTokens.Dchar) };

                        if (IsArray(curValueType))
                        {
                            flags = ObjectValueFlags.Array;

                            var clampDecl = curValueType as ArrayDecl;
                            var valueType = clampDecl.InnerDeclaration;

                            if (valueType is DTokenDeclaration)
                            {
                                bool IsString = false;
                                uint elsz = 0;
                                var realType = DetermineArrayType((valueType as DTokenDeclaration).Token, out elsz, out IsString);

                                var arr = Engine.Symbols.ReadArray(offset, realType, elsz);

                                if (arr != null)
                                    _valueString = BuildArrayContentString(arr, IsString);

                            }
                        }
                        else
                        {
                            flags = ObjectValueFlags.Object;
                        }
                    }
                }
            }

            return ObjectValue.CreatePrimitive(this, new ObjectPath(symbolname), _typeString, new EvaluationResult(_valueString), flags);
        }

        public static bool IsBasicType(ITypeDeclaration t)
        {
            return (t is DTokenDeclaration && DTokens.BasicTypes[(t as DTokenDeclaration).Token]);
        }

        public static bool IsBasicType(INode node)
        {
            return (node != null && node.Type is DTokenDeclaration && DTokens.BasicTypes[(node.Type as DTokenDeclaration).Token]);
        }

        public static bool IsArray(ITypeDeclaration t)
        {
            return t is ArrayDecl;
        }

        public static Type DetermineArrayType(int Token, out uint size, out bool IsString)
        {
            IsString = false;
            Type t = typeof(int);
            size = 4;
            switch (Token)
            {
                default:
                    break;
                case DTokens.Char:
                    IsString = true;
                    t = typeof(byte);
                    size = 1;
                    break;
                case DTokens.Wchar:
                    IsString = true;
                    t = typeof(ushort);
                    size = 2;
                    break;
                case DTokens.Dchar:
                    IsString = true;
                    t = typeof(uint);
                    size = 4;
                    break;

                case DTokens.Ubyte:
                    t = typeof(byte); size = 1;
                    break;
                case DTokens.Ushort:
                    t = typeof(ushort); size = 2;
                    break;
                case DTokens.Uint:
                    t = typeof(uint); size = 4;
                    break;
                case DTokens.Int:
                    t = typeof(int); size = 4;
                    break;
                case DTokens.Short:
                    t = typeof(short); size = 2;
                    break;
                case DTokens.Byte:
                    t = typeof(sbyte); size = 1;
                    break;
                case DTokens.Float:
                    t = typeof(float); size = 4;
                    break;
                case DTokens.Double:
                    t = typeof(double); size = 8;
                    break;
                case DTokens.Ulong:
                    t = typeof(ulong); size = 8;
                    break;
                case DTokens.Long:
                    t = typeof(long); size = 8;
                    break;
            }
            return t;
        }

        public static string BuildArrayContentString(object[] marr, bool IsString)
        {
            string str = "";
            if (marr != null)
            {
                var t = marr[0].GetType();
                if (IsString && !t.IsArray)
                {
                    try
                    {
                        str = "\"";
                        foreach (object o in marr)
                        {
                            if (o is uint)
                                str += Char.ConvertFromUtf32((int)(uint)o);
                            else if (o is UInt16)
                                str += (char)(ushort)o;
                            else if (o is byte)
                                str += (char)(byte)o;
                        }
                        str += "\"";
                    }
                    catch { str = "[Invalid / Not assigned]"; }

                }
                else
                {
                    str = "{";
                    foreach (object o in marr)
                    {
                        if (t.IsArray)
                            str += BuildArrayContentString((object[])o, IsString) + "; ";
                        else
                            str += o.ToString() + "; ";
                    }
                    str = str.Trim().TrimEnd(';') + "}";
                }
            }
            return str;
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


        ObjectValue CreateVarObject(string exp)
        {
            try
            {
                session.SelectThread(threadId);

                DebugEngineWrapper.DebugSymbolData[] datasymbols = Engine.Symbols.GetSymbols(exp);

                for (uint i = 0; i < Engine.Symbols.ScopeLocalSymbols.Count; i++)
                {
                    if (exp == Engine.Symbols.ScopeLocalSymbols.Symbols[i].Name)
                    {

                        session.RegisterTempVariableObject(exp);
                        return CreateObjectValue(exp, Engine.Symbols.ScopeLocalSymbols.Symbols[i]);
                    }
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

            ObjectValue val = FindParsedDVariable(symbol.Offset, vname, typeName, value, symbol.Parent);
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


            /*
			if (typeName.EndsWith ("]")) {
				val = ObjectValue.CreateArray (this, new ObjectPath (vname), typeName, nchild, flags, null);
			} else if (value == "{...}" || typeName.EndsWith ("*") || nchild > 0) {
				val = ObjectValue.CreateObject (this, new ObjectPath (vname), typeName, value, flags, null);
			} else {
				val = ObjectValue.CreatePrimitive (this, new ObjectPath (vname), typeName, new EvaluationResult (value), flags);
			}
			val.Name = name;
            */

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

                ObjectValue ov = FindParsedDVariable(offset, name, typename, val, child.Parent);
                if (ov == null)
                {
                    ObjectValueFlags flags = ObjectValueFlags.Variable;
                    ov = ObjectValue.CreatePrimitive(this, new ObjectPath(name), typename, new EvaluationResult(val), flags);
                }

                if (ov != null)
                    children.Add(ov);
            }

            return children.ToArray();

            /*
            DDebugCommandResult res = session.RunCommand("-var-list-children", "2", path.Join("."));
			ResultData cdata = res.GetObject ("children");
			
			// The response may not contain the "children" list at all.
			if (cdata == null)
				return children.ToArray ();
			
			if (index == -1) {
				index = 0;
				count = cdata.Count;
			}
			
			for (int n=index; n<cdata.Count && n<index+count; n++) {
				ResultData data = cdata.GetObject (n);
				ResultData child = data.GetObject ("child");
				
				string name = child.GetValue ("exp");
				if (name.Length > 0 && char.IsNumber (name [0]))
					name = "[" + name + "]";
				
				// C++ structures may contain typeless children named
				// "public", "private" and "protected".
				if (child.GetValue("type") == null) {
					ObjectPath childPath = new ObjectPath (child.GetValue ("name").Split ('.'));
					ObjectValue[] subchildren = GetChildren (childPath, -1, -1, options);
					children.AddRange(subchildren);
				} else {
					ObjectValue val = CreateObjectValue (name, child);
					children.Add (val);
				}
			}
			return children.ToArray ();
             */
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
