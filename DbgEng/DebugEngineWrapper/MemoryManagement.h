#pragma once
#include "stdafx.h"
#include <cliext/vector>

using namespace System;
using namespace cliext;

namespace DebugEngineWrapper
{
	public ref class DebugDataSpaces
	{
	internal:
		DbgDataSpaces* ds;
		DebugDataSpaces(DbgDataSpaces* dspaces):ds(dspaces){}
	public:
		array<BYTE>^ ReadVirtual(ULONG64 Offset,ULONG Size)
		{
			ULONG readb=0;
			BYTE *ret=new BYTE[Size];
			ds->ReadVirtual(Offset,&ret,Size,&readb);
			
			array<BYTE>^ ret2=gcnew array<BYTE>(readb);
			for(ULONG i=0;i<readb;i++)
			{
				ret2[i]=ret[i];
			}
			delete [] ret;
			return ret2;
		}
		
		BYTE* ReadArray(ULONG64 Offset, ULONG Size)
		{
			ULONG readb=0;
			BYTE *ret=new BYTE[Size];
			ds->ReadVirtual(Offset,&ret,Size,&readb);
			return ret;
		}

		USHORT ReadVirtualByte(ULONG64 Offset)
		{
			USHORT ret=0;
			ds->ReadVirtual(Offset,&ret,sizeof(USHORT),0);
			return ret;
		}

		ULONG ReadVirtualInt32(ULONG64 offset)
		{
			ULONG ret=0;
			ds->ReadVirtual(offset,&ret,sizeof(ULONG),0);
			return ret;
		}

		ULONG64 ReadVirtualInt64(ULONG64 offset)
		{
			ULONG64 ret=0;
			ds->ReadVirtual(offset,&ret,sizeof(ULONG64),0);
			return ret;
		}
	};
}