//  File: RTC6ADDONWrap.cs
//----------------------------------------------------------------------------
//  Copyright (c) 2016 by SCANLAB GmbH.                   All rights reserved.
//----------------------------------------------------------------------------
//
//
//  Abstract
//      Defines the RTC6ADDONWrap class that imports RTC6ADDON functions from RTC6ADDON's
//      dynamic-link library.
//      RTC6ADDONWrap automatically selects RTC6ADDON’s 64-bit version RTC6ADDONDLLx64.dll,
//      if the 64-bit runtime is in use. Otherwise, the 32-bit version
//      RTC6ADDONDLL.DLL is going to be selected. That is, RTC6ADDONWrap is good to
//      compile for the platform targets x86, x64, or 'Any CPU', where the
//      application, which is compiled for 'Any CPU' is able to run under
//      32-bit or 64-bit operating systems, as well.
//
//  Author
//      Bernhard Schrems, Christian Lutz
//
//      This file was automatically generated on Mrz 4, 2021
//
//  NOTE
//      THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
//      KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//      IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR
//      PURPOSE.
//
//----------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace RTC6ADDONImport
{
    /// <summary>
    /// Notice that the construction of the RTC6ADDONImport object or an initial
    /// call of any RTC6ADDONImport method may throw a TypeInitializationException
    /// exception, which indicates that the required DLL is missing or the
    /// import of a particular DLL function failed. In order to analyze and
    /// properly handle such an error condition you need to catch that
    /// TypeInitializationException type exception.
    /// </summary>
    public class RTC6ADDONWrap
    {
        const int TableSize = 1024;
        const int SampleArraySize = 1024*1024;
        const int SignalSize = 4;
        const int TransformSize = 132130;
        const int SignalSize2 = 8;

        const string DLL_NAMEx86 = @"package\\RTC6ADDONDLL.dll";     // DLL's 32-bit version.
        const string DLL_NAMEx64 = @"package\\RTC6ADDONDLLx64.dll";  // DLL's 64-bit version.

        class FunctionImporter
        {
            static string DllName;

            [DllImport("Kernel32.dll")]
            private extern static IntPtr LoadLibrary(string path);

            [DllImport("kernel32.dll")]
            public extern static bool FreeLibrary(IntPtr hModule);

            [DllImport("Kernel32.dll")]
            private extern static IntPtr GetProcAddress(IntPtr hModule,
                                                        string procName);

            static IntPtr hModule;

            static FunctionImporter instance = null;

            protected FunctionImporter(string DllName)
            {
                hModule = LoadLibrary(DllName);
            }

            ~FunctionImporter()
            {
                if (hModule != IntPtr.Zero)
                    FreeLibrary(hModule);
            }

            public static Delegate Import<T>(string functionName)
            {
                if (instance == null)
                {
                    DllName = (Marshal.SizeOf(typeof(IntPtr)) == 4) ? DLL_NAMEx86 : DLL_NAMEx64;
                    instance = new FunctionImporter(DllName);

                    if (hModule == IntPtr.Zero)
                        throw new System.IO.
                                FileNotFoundException(DllName + " not found. ");
                }
                var functionAddress = GetProcAddress(hModule, functionName);
                try
                {
                    return Marshal.
                        GetDelegateForFunctionPointer(functionAddress, typeof(T));
                }
                catch (Exception ex)
                {
                    if ((ex is ArgumentException) || (ex is ArgumentNullException))
                        throw new EntryPointNotFoundException(functionName);
                    else throw;
                }
            }
        }

        #region RTC6ADDONFunctionDelegates
        public delegate int activateShortVectorsDelegate([MarshalAs(UnmanagedType.LPArray, SizeConst=SignalSize)]uint[] Ptr);
        public delegate int initShortVectorsDelegate(out int Handler, double TimeLag, uint TransportDelay, double Speed);
        public delegate int n_initShortVectorsDelegate(uint CardNo, double TimeLag, uint TransportDelay, double Speed);
        public delegate int initShortVectorsSCANaDelegate(out int Handler, uint PreviewTime, uint Vmax, double Amax, double Speed);
        public delegate int n_initShortVectorsSCANaDelegate(uint CardNo, uint PreviewTime, uint Vmax, double Amax, double Speed);
        public delegate int setExtraPreRunTimeDelegate(int Handler, double ExtraPreRunTime);
        public delegate int n_setExtraPreRunTimeDelegate(uint CardNo, double ExtraPreRunTime);
        public delegate int setLaserPreTriggerTimeDelegate(int Handler, double LaserPreTriggerTime);
        public delegate int n_setLaserPreTriggerTimeDelegate(uint CardNo, double LaserPreTriggerTime);
        public delegate int setLaserSignalShiftSCANaDelegate(int Handler, double LaserSignalShift);
        public delegate int n_setLaserSignalShiftSCANaDelegate(uint CardNo, double LaserSignalShift);
        public delegate int setPreRunWaitTimeDelegate(int Handler, double PreRunWaitTime);
        public delegate int n_setPreRunWaitTimeDelegate(uint CardNo, double PreRunWaitTime);
        public delegate int setMinVectorLengthBitsDelegate(int Handler, uint MinVectorLengthBits);
        public delegate int n_setMinVectorLengthBitsDelegate(uint CardNo, uint MinVectorLengthBits);
        public delegate int setMinJumpLengthBitsDelegate(int Handler, uint MinJumpLengthBits);
        public delegate int n_setMinJumpLengthBitsDelegate(uint CardNo, uint MinJumpLengthBits);
        public delegate int addShortVectorCommandDelegate(int Handler, double xPos, double yPos, int Type);
        public delegate int n_addShortVectorCommandDelegate(uint CardNo, double xPos, double yPos, int Type);
        public delegate int addShortVectorCommand3DDelegate(int Handler, double xPos, double yPos, double zPos, int Type);
        public delegate int n_addShortVectorCommand3DDelegate(uint CardNo, double xPos, double yPos, double zPos, int Type);
        public delegate int finalizeShortVectorInputDelegate(int Handler);
        public delegate int n_finalizeShortVectorInputDelegate(uint CardNo);
        public delegate int setExecutionListSizeLimitDelegate(int Handler, int ExeListLimit);
        public delegate int n_setExecutionListSizeLimitDelegate(uint CardNo, int ExeListLimit);
        public delegate int getNeededListSpaceDelegate(int Handler);
        public delegate int n_getNeededListSpaceDelegate(uint CardNo);
        public delegate int sendShortVectorCommandsToRtcDelegate(int Handler);
        public delegate int n_sendShortVectorCommandsToRtcDelegate(uint CardNo);
        public delegate int resetShortVectorsDelegate(int Handler);
        public delegate int n_resetShortVectorsDelegate(uint CardNo);
        public delegate int freeShortVectorsDelegate(int Handler);
        public delegate int n_freeShortVectorsDelegate(uint CardNo);
        public delegate int freeAllShortVectorsDelegate();
        public delegate int identifyTransportDelayDelegate();
        public delegate int n_identifyTransportDelayDelegate(uint CardNo);
        public delegate double identifyTimeLagDelegate();
        public delegate double n_identifyTimeLagDelegate(uint CardNo);
        #endregion

        #region RTC6ADDONUserFunctions
        /// <summary>
        ///  int activateShortVectors(uint[] Ptr);
        /// </summary>
        public static activateShortVectorsDelegate activateShortVectors;

        /// <summary>
        ///  int initShortVectors(out int Handler, double TimeLag, uint TransportDelay, double Speed);
        /// </summary>
        public static initShortVectorsDelegate initShortVectors;

        /// <summary>
        ///  int n_initShortVectors(uint CardNo, double TimeLag, uint TransportDelay, double Speed);
        /// </summary>
        public static n_initShortVectorsDelegate n_initShortVectors;

        /// <summary>
        ///  int initShortVectorsSCANa(out int Handler, uint PreviewTime, uint Vmax, double Amax, double Speed);
        /// </summary>
        public static initShortVectorsSCANaDelegate initShortVectorsSCANa;

        /// <summary>
        ///  int n_initShortVectorsSCANa(uint CardNo, uint PreviewTime, uint Vmax, double Amax, double Speed);
        /// </summary>
        public static n_initShortVectorsSCANaDelegate n_initShortVectorsSCANa;

        /// <summary>
        ///  int setExtraPreRunTime(int Handler, double ExtraPreRunTime);
        /// </summary>
        public static setExtraPreRunTimeDelegate setExtraPreRunTime;

        /// <summary>
        ///  int n_setExtraPreRunTime(uint CardNo, double ExtraPreRunTime);
        /// </summary>
        public static n_setExtraPreRunTimeDelegate n_setExtraPreRunTime;

        /// <summary>
        ///  int setLaserPreTriggerTime(int Handler, double LaserPreTriggerTime);
        /// </summary>
        public static setLaserPreTriggerTimeDelegate setLaserPreTriggerTime;

        /// <summary>
        ///  int n_setLaserPreTriggerTime(uint CardNo, double LaserPreTriggerTime);
        /// </summary>
        public static n_setLaserPreTriggerTimeDelegate n_setLaserPreTriggerTime;

        /// <summary>
        ///  int setLaserSignalShiftSCANa(int Handler, double LaserSignalShift);
        /// </summary>
        public static setLaserSignalShiftSCANaDelegate setLaserSignalShiftSCANa;

        /// <summary>
        ///  int n_setLaserSignalShiftSCANa(uint CardNo, double LaserSignalShift);
        /// </summary>
        public static n_setLaserSignalShiftSCANaDelegate n_setLaserSignalShiftSCANa;

        /// <summary>
        ///  int setPreRunWaitTime(int Handler, double PreRunWaitTime);
        /// </summary>
        public static setPreRunWaitTimeDelegate setPreRunWaitTime;

        /// <summary>
        ///  int n_setPreRunWaitTime(uint CardNo, double PreRunWaitTime);
        /// </summary>
        public static n_setPreRunWaitTimeDelegate n_setPreRunWaitTime;

        /// <summary>
        ///  int setMinVectorLengthBits(int Handler, uint MinVectorLengthBits);
        /// </summary>
        public static setMinVectorLengthBitsDelegate setMinVectorLengthBits;

        /// <summary>
        ///  int n_setMinVectorLengthBits(uint CardNo, uint MinVectorLengthBits);
        /// </summary>
        public static n_setMinVectorLengthBitsDelegate n_setMinVectorLengthBits;

        /// <summary>
        ///  int setMinJumpLengthBits(int Handler, uint MinJumpLengthBits);
        /// </summary>
        public static setMinJumpLengthBitsDelegate setMinJumpLengthBits;

        /// <summary>
        ///  int n_setMinJumpLengthBits(uint CardNo, uint MinJumpLengthBits);
        /// </summary>
        public static n_setMinJumpLengthBitsDelegate n_setMinJumpLengthBits;

        /// <summary>
        ///  int addShortVectorCommand(int Handler, double xPos, double yPos, int Type);
        /// </summary>
        public static addShortVectorCommandDelegate addShortVectorCommand;

        /// <summary>
        ///  int n_addShortVectorCommand(uint CardNo, double xPos, double yPos, int Type);
        /// </summary>
        public static n_addShortVectorCommandDelegate n_addShortVectorCommand;

        /// <summary>
        ///  int addShortVectorCommand3D(int Handler, double xPos, double yPos, double zPos, int Type);
        /// </summary>
        public static addShortVectorCommand3DDelegate addShortVectorCommand3D;

        /// <summary>
        ///  int n_addShortVectorCommand3D(uint CardNo, double xPos, double yPos, double zPos, int Type);
        /// </summary>
        public static n_addShortVectorCommand3DDelegate n_addShortVectorCommand3D;

        /// <summary>
        ///  int finalizeShortVectorInput(int Handler);
        /// </summary>
        public static finalizeShortVectorInputDelegate finalizeShortVectorInput;

        /// <summary>
        ///  int n_finalizeShortVectorInput(uint CardNo);
        /// </summary>
        public static n_finalizeShortVectorInputDelegate n_finalizeShortVectorInput;

        /// <summary>
        ///  int setExecutionListSizeLimit(int Handler, int ExeListLimit);
        /// </summary>
        public static setExecutionListSizeLimitDelegate setExecutionListSizeLimit;

        /// <summary>
        ///  int n_setExecutionListSizeLimit(uint CardNo, int ExeListLimit);
        /// </summary>
        public static n_setExecutionListSizeLimitDelegate n_setExecutionListSizeLimit;

        /// <summary>
        ///  int getNeededListSpace(int Handler);
        /// </summary>
        public static getNeededListSpaceDelegate getNeededListSpace;

        /// <summary>
        ///  int n_getNeededListSpace(uint CardNo);
        /// </summary>
        public static n_getNeededListSpaceDelegate n_getNeededListSpace;

        /// <summary>
        ///  int sendShortVectorCommandsToRtc(int Handler);
        /// </summary>
        public static sendShortVectorCommandsToRtcDelegate sendShortVectorCommandsToRtc;

        /// <summary>
        ///  int n_sendShortVectorCommandsToRtc(uint CardNo);
        /// </summary>
        public static n_sendShortVectorCommandsToRtcDelegate n_sendShortVectorCommandsToRtc;

        /// <summary>
        ///  int resetShortVectors(int Handler);
        /// </summary>
        public static resetShortVectorsDelegate resetShortVectors;

        /// <summary>
        ///  int n_resetShortVectors(uint CardNo);
        /// </summary>
        public static n_resetShortVectorsDelegate n_resetShortVectors;

        /// <summary>
        ///  int freeShortVectors(int Handler);
        /// </summary>
        public static freeShortVectorsDelegate freeShortVectors;

        /// <summary>
        ///  int n_freeShortVectors(uint CardNo);
        /// </summary>
        public static n_freeShortVectorsDelegate n_freeShortVectors;

        /// <summary>
        ///  int freeAllShortVectors();
        /// </summary>
        public static freeAllShortVectorsDelegate freeAllShortVectors;

        /// <summary>
        ///  int identifyTransportDelay();
        /// </summary>
        public static identifyTransportDelayDelegate identifyTransportDelay;

        /// <summary>
        ///  int n_identifyTransportDelay(uint CardNo);
        /// </summary>
        public static n_identifyTransportDelayDelegate n_identifyTransportDelay;

        /// <summary>
        ///  double identifyTimeLag();
        /// </summary>
        public static identifyTimeLagDelegate identifyTimeLag;

        /// <summary>
        ///  double n_identifyTimeLag(uint CardNo);
        /// </summary>
        public static n_identifyTimeLagDelegate n_identifyTimeLag;

        #endregion

        // Notice that the static constructor is used to initialize any static data,
        //    or to perform a particular action that needs to be performed once only.
        //    It is called automatically before the first instance is created or any
        //    static members are referenced.
        static RTC6ADDONWrap()
        {
            // Import functions and set them up as delegates.
            //
            #region DLLFunctionImport
            activateShortVectors = (activateShortVectorsDelegate)FunctionImporter.Import<activateShortVectorsDelegate>("activateShortVectors");
            initShortVectors = (initShortVectorsDelegate)FunctionImporter.Import<initShortVectorsDelegate>("initShortVectors");
            n_initShortVectors = (n_initShortVectorsDelegate)FunctionImporter.Import<n_initShortVectorsDelegate>("n_initShortVectors");
            initShortVectorsSCANa = (initShortVectorsSCANaDelegate)FunctionImporter.Import<initShortVectorsSCANaDelegate>("initShortVectorsSCANa");
            n_initShortVectorsSCANa = (n_initShortVectorsSCANaDelegate)FunctionImporter.Import<n_initShortVectorsSCANaDelegate>("n_initShortVectorsSCANa");
            setExtraPreRunTime = (setExtraPreRunTimeDelegate)FunctionImporter.Import<setExtraPreRunTimeDelegate>("setExtraPreRunTime");
            n_setExtraPreRunTime = (n_setExtraPreRunTimeDelegate)FunctionImporter.Import<n_setExtraPreRunTimeDelegate>("n_setExtraPreRunTime");
            setLaserPreTriggerTime = (setLaserPreTriggerTimeDelegate)FunctionImporter.Import<setLaserPreTriggerTimeDelegate>("setLaserPreTriggerTime");
            n_setLaserPreTriggerTime = (n_setLaserPreTriggerTimeDelegate)FunctionImporter.Import<n_setLaserPreTriggerTimeDelegate>("n_setLaserPreTriggerTime");
            setLaserSignalShiftSCANa = (setLaserSignalShiftSCANaDelegate)FunctionImporter.Import<setLaserSignalShiftSCANaDelegate>("setLaserSignalShiftSCANa");
            n_setLaserSignalShiftSCANa = (n_setLaserSignalShiftSCANaDelegate)FunctionImporter.Import<n_setLaserSignalShiftSCANaDelegate>("n_setLaserSignalShiftSCANa");
            setPreRunWaitTime = (setPreRunWaitTimeDelegate)FunctionImporter.Import<setPreRunWaitTimeDelegate>("setPreRunWaitTime");
            n_setPreRunWaitTime = (n_setPreRunWaitTimeDelegate)FunctionImporter.Import<n_setPreRunWaitTimeDelegate>("n_setPreRunWaitTime");
            setMinVectorLengthBits = (setMinVectorLengthBitsDelegate)FunctionImporter.Import<setMinVectorLengthBitsDelegate>("setMinVectorLengthBits");
            n_setMinVectorLengthBits = (n_setMinVectorLengthBitsDelegate)FunctionImporter.Import<n_setMinVectorLengthBitsDelegate>("n_setMinVectorLengthBits");
            setMinJumpLengthBits = (setMinJumpLengthBitsDelegate)FunctionImporter.Import<setMinJumpLengthBitsDelegate>("setMinJumpLengthBits");
            n_setMinJumpLengthBits = (n_setMinJumpLengthBitsDelegate)FunctionImporter.Import<n_setMinJumpLengthBitsDelegate>("n_setMinJumpLengthBits");
            addShortVectorCommand = (addShortVectorCommandDelegate)FunctionImporter.Import<addShortVectorCommandDelegate>("addShortVectorCommand");
            n_addShortVectorCommand = (n_addShortVectorCommandDelegate)FunctionImporter.Import<n_addShortVectorCommandDelegate>("n_addShortVectorCommand");
            addShortVectorCommand3D = (addShortVectorCommand3DDelegate)FunctionImporter.Import<addShortVectorCommand3DDelegate>("addShortVectorCommand3D");
            n_addShortVectorCommand3D = (n_addShortVectorCommand3DDelegate)FunctionImporter.Import<n_addShortVectorCommand3DDelegate>("n_addShortVectorCommand3D");
            finalizeShortVectorInput = (finalizeShortVectorInputDelegate)FunctionImporter.Import<finalizeShortVectorInputDelegate>("finalizeShortVectorInput");
            n_finalizeShortVectorInput = (n_finalizeShortVectorInputDelegate)FunctionImporter.Import<n_finalizeShortVectorInputDelegate>("n_finalizeShortVectorInput");
            setExecutionListSizeLimit = (setExecutionListSizeLimitDelegate)FunctionImporter.Import<setExecutionListSizeLimitDelegate>("setExecutionListSizeLimit");
            n_setExecutionListSizeLimit = (n_setExecutionListSizeLimitDelegate)FunctionImporter.Import<n_setExecutionListSizeLimitDelegate>("n_setExecutionListSizeLimit");
            getNeededListSpace = (getNeededListSpaceDelegate)FunctionImporter.Import<getNeededListSpaceDelegate>("getNeededListSpace");
            n_getNeededListSpace = (n_getNeededListSpaceDelegate)FunctionImporter.Import<n_getNeededListSpaceDelegate>("n_getNeededListSpace");
            sendShortVectorCommandsToRtc = (sendShortVectorCommandsToRtcDelegate)FunctionImporter.Import<sendShortVectorCommandsToRtcDelegate>("sendShortVectorCommandsToRtc");
            n_sendShortVectorCommandsToRtc = (n_sendShortVectorCommandsToRtcDelegate)FunctionImporter.Import<n_sendShortVectorCommandsToRtcDelegate>("n_sendShortVectorCommandsToRtc");
            resetShortVectors = (resetShortVectorsDelegate)FunctionImporter.Import<resetShortVectorsDelegate>("resetShortVectors");
            n_resetShortVectors = (n_resetShortVectorsDelegate)FunctionImporter.Import<n_resetShortVectorsDelegate>("n_resetShortVectors");
            freeShortVectors = (freeShortVectorsDelegate)FunctionImporter.Import<freeShortVectorsDelegate>("freeShortVectors");
            n_freeShortVectors = (n_freeShortVectorsDelegate)FunctionImporter.Import<n_freeShortVectorsDelegate>("n_freeShortVectors");
            freeAllShortVectors = (freeAllShortVectorsDelegate)FunctionImporter.Import<freeAllShortVectorsDelegate>("freeAllShortVectors");
            identifyTransportDelay = (identifyTransportDelayDelegate)FunctionImporter.Import<identifyTransportDelayDelegate>("identifyTransportDelay");
            n_identifyTransportDelay = (n_identifyTransportDelayDelegate)FunctionImporter.Import<n_identifyTransportDelayDelegate>("n_identifyTransportDelay");
            identifyTimeLag = (identifyTimeLagDelegate)FunctionImporter.Import<identifyTimeLagDelegate>("identifyTimeLag");
            n_identifyTimeLag = (n_identifyTimeLagDelegate)FunctionImporter.Import<n_identifyTimeLagDelegate>("n_identifyTimeLag");
            #endregion
        }
  }
}
