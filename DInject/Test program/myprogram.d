module myprogram;

import std.conv;
import std.cstream;
import std.stdio;

__gshared int p;

extern(C) void toSt()
{
	string s;
	asm{ nop;}
	s= (cast(Object)cast(void*)p).toString();
	asm{
		nop;nop;
	}
	writeln(s);
}

class myClass
{
	int a=100;
	
	override string toString()
	{
		return to!string(a++);
	}
}

extern(Windows) export void toSt2(int* pp)
{
	/*
	*pp = 
		cast(int) 
		cast(void*) 
		(
		cast(Object)
		cast(void*)
		*pp)
		.toString();	*/
}

void main()
{
	int a=23;
	asm
	{
		nop;
		nop;
		nop;
		nop;
	}
	
	if(a == 24)
		toSt();
	
	asm{
		nop;	
	}
	a = 0x1234;
	
	/*Object o = new myClass();
	
	int pp = cast(int)cast(void*)o;
	toSt2(&pp);
	
	write("Variable address: ");
	writeln(pp);
	
	write("Char count: ");
	writeln(c);
	
	din.getc();
	*/
	asm{
		nop;nop;nop;
	}
	auto o = new myClass();
	asm{
		nop;nop;nop;
	}
	
	while(true)
	{
		//toSt(cast(int)cast(void*)o);
		writeln(cast(Object)o);
		writeln(cast(int)cast(void*)o);
		if(din.getc()=='q')
			break;
	}
}

