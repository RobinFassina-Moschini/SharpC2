﻿// Author: Ryan Cobb (@cobbr_io)
// Project: SharpSploit (https://github.com/cobbr/SharpSploit)
// License: BSD 3-Clause

using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DroneService.Invocation.DynamicInvoke
{
    public static class Generic
    {
        public static object DynamicAPIInvoke(string DLLName, string FunctionName, Type FunctionDelegateType, ref object[] Parameters, bool CanLoadFromDisk = false, bool ResolveForwards = true)
        {
            var pFunction = GetLibraryAddress(DLLName, FunctionName, CanLoadFromDisk, ResolveForwards);
            return DynamicFunctionInvoke(pFunction, FunctionDelegateType, ref Parameters);
        }

        public static object DynamicFunctionInvoke(IntPtr FunctionPointer, Type FunctionDelegateType, ref object[] Parameters)
        {
            var funcDelegate = Marshal.GetDelegateForFunctionPointer(FunctionPointer, FunctionDelegateType);
            return funcDelegate.DynamicInvoke(Parameters);
        }

        public static IntPtr LoadModuleFromDisk(string DLLPath)
        {
            var uModuleName = new Data.Native.UNICODE_STRING();
            Native.RtlInitUnicodeString(ref uModuleName, DLLPath);

            var hModule = IntPtr.Zero;
            var CallResult = Native.LdrLoadDll(IntPtr.Zero, 0, ref uModuleName, ref hModule);
            if (CallResult != 0 || hModule == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return hModule;
        }

        public static IntPtr GetLibraryAddress(string DLLName, string FunctionName, bool CanLoadFromDisk = false, bool ResolveForwards = true)
        {
            var hModule = GetLoadedModuleAddress(DLLName);
            if (hModule == IntPtr.Zero && CanLoadFromDisk)
            {
                hModule = LoadModuleFromDisk(DLLName);
                if (hModule == IntPtr.Zero)
                {
                    throw new FileNotFoundException(DLLName + ", unable to find the specified file.");
                }
            }
            else if (hModule == IntPtr.Zero)
            {
                throw new DllNotFoundException(DLLName + ", Dll was not found.");
            }

            return GetExportAddress(hModule, FunctionName, ResolveForwards);
        }

        public static IntPtr GetLoadedModuleAddress(string DLLName)
        {
            var ProcModules = Process.GetCurrentProcess().Modules;
            foreach (ProcessModule Mod in ProcModules)
            {
                if (Mod.FileName.ToLower().EndsWith(DLLName.ToLower()))
                {
                    return Mod.BaseAddress;
                }
            }
            return IntPtr.Zero;
        }

        public static IntPtr GetPebLdrModuleEntry(string DLLName)
        {
            // Get _PEB pointer
            var pbi = Native.NtQueryInformationProcessBasicInformation((IntPtr)(-1));

            // Set function variables
            UInt32 LdrDataOffset = 0;
            UInt32 InLoadOrderModuleListOffset = 0;
            if (IntPtr.Size == 4)
            {
                LdrDataOffset = 0xc;
                InLoadOrderModuleListOffset = 0xC;
            }
            else
            {
                LdrDataOffset = 0x18;
                InLoadOrderModuleListOffset = 0x10;
            }

            // Get module InLoadOrderModuleList -> _LIST_ENTRY
            var PEB_LDR_DATA = Marshal.ReadIntPtr((IntPtr)((UInt64)pbi.PebBaseAddress + LdrDataOffset));
            var pInLoadOrderModuleList = (IntPtr)((UInt64)PEB_LDR_DATA + InLoadOrderModuleListOffset);
            var le = (Data.Native.LIST_ENTRY)Marshal.PtrToStructure(pInLoadOrderModuleList, typeof(Data.Native.LIST_ENTRY));

            // Loop entries
            var flink = le.Flink;
            var hModule = IntPtr.Zero;
            var dte = (Data.PE.LDR_DATA_TABLE_ENTRY)Marshal.PtrToStructure(flink, typeof(Data.PE.LDR_DATA_TABLE_ENTRY));
            while (dte.InLoadOrderLinks.Flink != le.Blink)
            {
                // Match module name
                if (Marshal.PtrToStringUni(dte.FullDllName.Buffer).EndsWith(DLLName, StringComparison.OrdinalIgnoreCase))
                {
                    hModule = dte.DllBase;
                }
            
                // Move Ptr
                flink = dte.InLoadOrderLinks.Flink;
                dte = (Data.PE.LDR_DATA_TABLE_ENTRY)Marshal.PtrToStructure(flink, typeof(Data.PE.LDR_DATA_TABLE_ENTRY));
            }

            return hModule;
        }

        public static IntPtr GetExportAddress(IntPtr ModuleBase, string ExportName, bool ResolveForwards = true)
        {
            var FunctionPtr = IntPtr.Zero;
            try
            {
                // Traverse the PE header in memory
                var PeHeader = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + 0x3C));
                var OptHeaderSize = Marshal.ReadInt16((IntPtr)(ModuleBase.ToInt64() + PeHeader + 0x14));
                var OptHeader = ModuleBase.ToInt64() + PeHeader + 0x18;
                var Magic = Marshal.ReadInt16((IntPtr)OptHeader);
                Int64 pExport = 0;
                if (Magic == 0x010b)
                {
                    pExport = OptHeader + 0x60;
                }
                else
                {
                    pExport = OptHeader + 0x70;
                }

                // Read -> IMAGE_EXPORT_DIRECTORY
                var ExportRVA = Marshal.ReadInt32((IntPtr)pExport);
                var OrdinalBase = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x10));
                var NumberOfFunctions = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x14));
                var NumberOfNames = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x18));
                var FunctionsRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x1C));
                var NamesRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x20));
                var OrdinalsRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x24));

                // Get the VAs of the name table's beginning and end.
                var NamesBegin = ModuleBase.ToInt64() + Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + NamesRVA));
                var NamesFinal = NamesBegin + NumberOfNames * 4;

                // Loop the array of export name RVA's
                for (var i = 0; i < NumberOfNames; i++)
                {
                    var FunctionName = Marshal.PtrToStringAnsi((IntPtr)(ModuleBase.ToInt64() + Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + NamesRVA + i * 4))));
                    
                    if (FunctionName.Equals(ExportName, StringComparison.OrdinalIgnoreCase))
                    {

                        var FunctionOrdinal = Marshal.ReadInt16((IntPtr)(ModuleBase.ToInt64() + OrdinalsRVA + i * 2)) + OrdinalBase;
                        var FunctionRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + FunctionsRVA + (4 * (FunctionOrdinal - OrdinalBase))));
                        FunctionPtr = (IntPtr)((Int64)ModuleBase + FunctionRVA);
                        
                        if (ResolveForwards == true)
                            // If the export address points to a forward, get the address
                            FunctionPtr = GetForwardAddress(FunctionPtr);

                        break;
                    }
                }
            }
            catch
            {
                // Catch parser failure
                throw new InvalidOperationException("Failed to parse module exports.");
            }

            if (FunctionPtr == IntPtr.Zero)
            {
                // Export not found
                throw new MissingMethodException(ExportName + ", export not found.");
            }
            return FunctionPtr;
        }

        public static IntPtr GetForwardAddress(IntPtr ExportAddress, bool CanLoadFromDisk = false)
        {
            var FunctionPtr = ExportAddress;
            try
            {
                // Assume it is a forward. If it is not, we will get an error
                var ForwardNames = Marshal.PtrToStringAnsi(FunctionPtr);
                var values = ForwardNames.Split('.');

                if (values.Length > 1)
                {
                    var ForwardModuleName = values[0];
                    var ForwardExportName = values[1];

                    // Check if it is an API Set mapping
                    var ApiSet = GetApiSetMapping();
                    var LookupKey = ForwardModuleName.Substring(0, ForwardModuleName.Length - 2) + ".dll";
                    if (ApiSet.ContainsKey(LookupKey))
                        ForwardModuleName = ApiSet[LookupKey];
                    else
                        ForwardModuleName = ForwardModuleName + ".dll";

                    var hModule = GetPebLdrModuleEntry(ForwardModuleName);
                    if (hModule == IntPtr.Zero && CanLoadFromDisk == true)
                        hModule = LoadModuleFromDisk(ForwardModuleName);
                    if (hModule != IntPtr.Zero)
                    {
                        FunctionPtr = GetExportAddress(hModule, ForwardExportName);
                    }
                }
            }
            catch
            {
                // Do nothing, it was not a forward
            }
            return FunctionPtr;
        }
        
        public static Dictionary<string, string> GetApiSetMapping()
        {
            var pbi = Native.NtQueryInformationProcessBasicInformation((IntPtr)(-1));
            var ApiSetMapOffset = IntPtr.Size == 4 ? (UInt32)0x38 : 0x68;

            // Create mapping dictionary
            var ApiSetDict = new Dictionary<string, string>();

            var pApiSetNamespace = Marshal.ReadIntPtr((IntPtr)((UInt64)pbi.PebBaseAddress + ApiSetMapOffset));
            var Namespace = (Data.PE.ApiSetNamespace)Marshal.PtrToStructure(pApiSetNamespace, typeof(Data.PE.ApiSetNamespace));
            for (var i = 0; i < Namespace.Count; i++)
            {
                var SetEntry = new Data.PE.ApiSetNamespaceEntry();
                var pSetEntry = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)Namespace.EntryOffset + (UInt64)(i * Marshal.SizeOf(SetEntry)));
                SetEntry = (Data.PE.ApiSetNamespaceEntry)Marshal.PtrToStructure(pSetEntry, typeof(Data.PE.ApiSetNamespaceEntry));

                var ApiSetEntryName = Marshal.PtrToStringUni((IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.NameOffset), SetEntry.NameLength/2);
                var ApiSetEntryKey = ApiSetEntryName.Substring(0, ApiSetEntryName.Length - 2) + ".dll" ; // Remove the patch number and add .dll

                var SetValue = new Data.PE.ApiSetValueEntry();

                var pSetValue = IntPtr.Zero;

                // If there is only one host, then use it
                if (SetEntry.ValueLength == 1)
                    pSetValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset);
                else if (SetEntry.ValueLength > 1)
                {
                    // Loop through the hosts until we find one that is different from the key, if available
                    for (var j = 0; j < SetEntry.ValueLength; j++)
                    {
                        var host = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset + (UInt64)Marshal.SizeOf(SetValue) * (UInt64)j);
                        if (Marshal.PtrToStringUni(host) != ApiSetEntryName)
                            pSetValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset + (UInt64)Marshal.SizeOf(SetValue) * (UInt64)j);
                    }
                    // If there is not one different from the key, then just use the key and hope that works
                    if (pSetValue == IntPtr.Zero)
                        pSetValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset);
                }

                //Get the host DLL's name from the entry
                SetValue = (Data.PE.ApiSetValueEntry)Marshal.PtrToStructure(pSetValue, typeof(Data.PE.ApiSetValueEntry));
                var ApiSetValue = string.Empty;
                if (SetValue.ValueCount != 0)
                {
                    var pValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetValue.ValueOffset);
                    ApiSetValue = Marshal.PtrToStringUni(pValue, SetValue.ValueCount/2);
                }

                // Add pair to dict
                ApiSetDict.Add(ApiSetEntryKey, ApiSetValue);
            }

            // Return dict
            return ApiSetDict;
        }
    }
}