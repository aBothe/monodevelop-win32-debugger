using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MonoDevelop.Ide;
using Mono.Debugging.Client;
using Mono.Debugging.Backend;
using D_Parser.Dom;
using D_Parser.Dom.Statements;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver;
using D_Parser.Parser;
using D_Parser.Misc;
using MonoDevelop.D;


using DEW = DebugEngineWrapper;
using MonoDevelop.D.Projects;
using MonoDevelop.D.Completion;
using MonoDevelop.D.Building;

namespace MonoDevelop.D.DDebugger.DbgEng
{
	class MonoDSymbolResolver
	{
		private DEW.DBGEngine Engine;
		private IObjectValueSource ObjectValueSource;

		public MonoDSymbolResolver(IObjectValueSource objectValueSource, DEW.DBGEngine engine)
		{
			this.ObjectValueSource = objectValueSource;
			this.Engine = engine;
		}

		public ObjectValue Resolve(DEW.DebugScopedSymbol symbol)
		{
			return Resolve(symbol.Offset, symbol.Name, symbol.TypeName, symbol.TextValue, symbol.Parent);
		}

		public ObjectValue Resolve(ulong offset, string symbolname, string typename, string val, DEW.DebugScopedSymbol parentsymbol)
		{
			DModule module;
			int codeLine;
			INode variableNode = null;

			// Search currently scoped module
			string file = "";
			uint line = 0;
			Engine.Symbols.GetLineByOffset(Engine.CurrentInstructionOffset, out file, out line);
			codeLine = (int)line;

			if (string.IsNullOrWhiteSpace(file))
				return null;

			AbstractDProject dproj = null;
			module = GetFileSyntaxTree(file, out dproj);

			// If syntax tree built, search the variable location
			if (module != null)
			{
				IStatement stmt;
				var block = DResolver.SearchBlockAt(module, new CodeLocation(0, codeLine), out stmt);
				
				var ctxt = ResolutionContext.Create(dproj != null ? dproj.ParseCache : DCompilerService.Instance.GetDefaultCompiler().GenParseCacheView(), null, block, stmt);

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

			return ObjectValue.CreatePrimitive(ObjectValueSource, new ObjectPath(symbolname), _typeString, new EvaluationResult(_valueString), flags);
		}

		private AbstractType[] ResolveParentSymbol(DEW.DebugScopedSymbol parentsymbol, ResolutionContext ctxt)
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


		public static DModule GetFileSyntaxTree(string file, out AbstractDProject OwnerProject)
		{
			OwnerProject = IdeApp.Workbench.ActiveDocument.Project as AbstractDProject;
			return GlobalParseCache.GetModule(file);
		}
	}
}
