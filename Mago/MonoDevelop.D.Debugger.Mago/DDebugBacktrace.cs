
using System;
using System.Collections.Generic;
using System.Globalization;
using Mono.Debugging.Client;
using Mono.Debugging.Backend;

using MagoWrapper;

namespace MonoDevelop.D.DDebugger.Mago
{
    internal class DDebugExceptionBackTrace : DDebugBacktrace
    {
        private ExceptionRecord exceptionRecord;

        public DDebugExceptionBackTrace(ExceptionRecord rec, DDebugSession session, uint threadId, Debuggee debuggee)
            : base(session, threadId, debuggee)
        {
            this.exceptionRecord = rec;

        }

        public override ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
        {

            ObjectValue val = CreateExceptionObject(exceptionRecord);
            ExceptionInfo result = new ExceptionInfo(val);

            return result;
        }

        private ObjectValue CreateExceptionObject(ExceptionRecord expRec)
        {
            List<ObjectValue> children = new List<ObjectValue>();

            //message
            ObjectValue message = ObjectValue.CreateObject(
                this,
                new ObjectPath(new string[] { expRec.ExceptionName }),
                "string",
                expRec.ExceptionInfo,
                ObjectValueFlags.Object,
                new ObjectValue[0]);
            message.Name = "Message";
            children.Add(message);

            //inner exception
            if (expRec.InnerExceptionRecord != null)
            {
                ObjectValue innerExp = CreateExceptionObject(expRec.InnerExceptionRecord);
                innerExp.Name = "InnerException";
                children.Add(innerExp);
            }

            //do these for first level only?
            if (expRec == exceptionRecord)
            {
                //stack trace
                List<ObjectValue> stackList = new List<ObjectValue>();
                for (int i = 0; i < magoCallStackFrames.Count; i++)
                {
                    List<ObjectValue> stkValChildren = new List<ObjectValue>();

                    // line
                    ObjectValue stkValLine = ObjectValue.CreateObject(
                        this,
                        new ObjectPath(new string[] { expRec.ExceptionName, "StackTrace", i.ToString(CultureInfo.InvariantCulture) }),
                        "Int",
                        i.ToString(CultureInfo.InvariantCulture),
                        ObjectValueFlags.Object,
                        new ObjectValue[0]);
                    stkValLine.Name = "Line";
                    stkValChildren.Add(stkValLine);

                    ObjectValue stkVal = ObjectValue.CreateObject(
                        this,
                        new ObjectPath(new string[] { expRec.ExceptionName, "StackTrace" }),
                        "Object",
                        magoCallStackFrames[i].FunctionName,
                        ObjectValueFlags.Object,
                        stkValChildren.ToArray());
                    stackList.Add(stkVal);
                }

                ObjectValue stacktrace = ObjectValue.CreateArray(
                    this,
                    new ObjectPath(new string[] {expRec.ExceptionName}),
                    "StackTrace",
                    magoCallStackFrames.Count,
                    ObjectValueFlags.Array,
                    stackList.ToArray());
                stacktrace.Name = "StackTrace";
                children.Add(stacktrace);

                //instance message
                List<ObjectValue> instanceChildren = new List<ObjectValue>();
                ObjectValue instanceMessage = ObjectValue.CreateObject(
                    this,
                    new ObjectPath(new string[] { expRec.ExceptionName, "Instance" }),
                    "string",
                    expRec.ExceptionInfo,
                    ObjectValueFlags.Object | ObjectValueFlags.Public,
                    new ObjectValue[0]);
                instanceMessage.Name = "Message";
                instanceChildren.Add(instanceMessage);

                //instance
                ObjectValue instance = ObjectValue.CreateObject(
                        this,
                        new ObjectPath(new string[] { expRec.ExceptionName }),
                        expRec.ExceptionName,
                        expRec.ExceptionName,
                        ObjectValueFlags.Object,
                        instanceChildren.ToArray());
                instance.Name = "Instance";
                children.Add(instance);
            }

            //parent
            ObjectValue val = ObjectValue.CreateObject(
                this,
                new ObjectPath(new string[] {}),
                expRec.ExceptionName,
                String.Empty,
                ObjectValueFlags.Error, children.ToArray());           

            return val;
        }
    }

    class DDebugBacktrace : IBacktrace, IObjectValueSource
    {
        int fcount;
        DDebugSession session;
        DissassemblyBuffer[] disBuffers;
        uint threadId;
        Debuggee debuggee;
        protected List<CallStackFrame> magoCallStackFrames;
        List<DebugScopedSymbol> symbols;


        public DDebugBacktrace(DDebugSession session, uint threadId, Debuggee debuggee)
        {
            this.session = session;
            this.debuggee = debuggee;
            this.threadId = threadId;

            magoCallStackFrames = debuggee.GetCallStack(threadId);
            fcount = magoCallStackFrames.Count;

            symbols = this.session.SymbolResolver.GetLocalSymbols(threadId);
        }

        ~DDebugBacktrace()
        {

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

            session.SelectThread(threadId);
            for (int i = firstIndex; i < Math.Min(lastIndex + 1, magoCallStackFrames.Count); i++)
            {
                frames.Add(CreateFrame(magoCallStackFrames[i]));
            }

            return frames.ToArray();
        }

        public ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
        {
            List<ObjectValue> values = new List<ObjectValue>();

            if (frameIndex >= magoCallStackFrames.Count)
                return values.ToArray();

            List<DebugScopedSymbol> tempSymbols = null;
            if (frameIndex == 0)
                tempSymbols = symbols;
            else
                tempSymbols = session.SymbolResolver.GetLocalSymbols(threadId, magoCallStackFrames[frameIndex].InstructionPointer);

            foreach (var symbol in symbols)
            {
                ObjectValueFlags flags = ObjectValueFlags.Object;
                ObjectValue ov = ObjectValue.CreatePrimitive(this, new ObjectPath(symbol.FullName.Split(".".ToCharArray())), symbol.TypeName, new EvaluationResult(symbol.TextValue), flags);
                values.Add(ov);
            }

            return values.ToArray();
        }

        public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
        {
            List<ObjectValue> children = new List<ObjectValue>();
            session.SelectThread(threadId);

            string expression = path.Join(".");

            if (expression.Trim().Length == 0)
                return children.ToArray();

            List<DebugScopedSymbol> childSymbols = this.session.SymbolResolver.GetChildSymbols(expression, threadId);
            if (childSymbols.Count == 0)
                return children.ToArray();

            for (int i = 0; i < childSymbols.Count; i++)
            {
                DebugScopedSymbol child = childSymbols[i];

                ObjectValue ov = CreateObjectValue(child);
                children.Add(ov);
            }

            return children.ToArray();
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

        public virtual ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
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

                foreach (var symbol in symbols)
                {
                    if (symbol.Name.ToLower() == exp.ToLower())
                    {
                        return CreateObjectValue(symbol);
                    }
                }

                DebugScopedSymbol evaluatedSymbol = this.session.SymbolResolver.Evaluate(exp, threadId);
                if (evaluatedSymbol != null)
                {
                    session.RegisterTempVariableObject(exp);
                    return CreateObjectValue(evaluatedSymbol);
                }

                return ObjectValue.CreateUnknown(exp);
            }
            catch (Exception ex)
            {
                return ObjectValue.CreateFatalError(exp, ex.Message, ObjectValueFlags.Error);
            }
        }

        private ObjectValue CreateObjectValue(DebugScopedSymbol symbol)
        {

            ObjectValueFlags flags = CreateObjectValueFlags(symbol);
            ObjectValue val = ObjectValue.CreatePrimitive(this, new ObjectPath(symbol.FullName.Split(".".ToCharArray())), symbol.TypeName, new EvaluationResult(symbol.TextValue), flags);

            return val;
        }

        private ObjectValueFlags CreateObjectValueFlags(DebugScopedSymbol symbol)
        {
            switch (symbol.TypeName.ToLower())
            {
                case "bit":
                case "char":
                case "byte":
                case "ubyte":
                case "short":
                case "ushort":
                case "wchar":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "real":
                case "ireal":
                case "creal":
                case "void*":
                    return ObjectValueFlags.Primitive;
            }

            return ObjectValueFlags.Object;
        }

        public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
        {
            throw new NotSupportedException();
        }

        public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
        {
            throw new NotSupportedException();
        }

        void SelectFrame(int frame)
        {
            session.SelectThread(threadId);
        }

        StackFrame CreateFrame(CallStackFrame magoFrame)
        {
            string fn;
            uint ln;
            ulong address = magoFrame.InstructionPointer;
            session.SymbolResolver.GetCodeLineFromAddress(address, out fn, out ln);


            //string methodName = session.SymbolResolver.GetFunctionNameFromAddress(address, threadId);
            string methodName = magoFrame.FunctionName;
            SourceLocation loc = new SourceLocation(methodName, fn, (int)ln);

            return new StackFrame((long)address, loc, "Native");
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
