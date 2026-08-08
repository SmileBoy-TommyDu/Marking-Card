using System;
using System.Runtime.InteropServices;
//using Tools;
using System.Net;
using System.Threading;
//using NUnit.Framework;
using System.Text;
using System.ComponentModel;
using System.IO;

namespace DrSoft.MarkCard.BoChu
{
    public class InvokeGalvoApiDll
    {
        // 指向运行目录下的 package 子目录中的 dll（P/Invoke 特性要求编译时常量）
        public const string DllPath = @"package\\GalvoApi2.Dll";

        // 在运行时把 "package" 目录加入到 DLL 搜索路径，确保加载成功
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        static InvokeGalvoApiDll()
        {
            try
            {
                string packageDir = Path.Combine(AppContext.BaseDirectory, "package");
                SetDllDirectory(packageDir);
            }
            catch
            {
                // 忽略失败，让后续加载继续按默认搜索规则尝试
            }
        }
        //DLL接口导入

        #region 立即指令(扫卡)
        /// <summary>开始扫描振镜卡</summary>
        /// <param name="Count">振镜卡数量</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_BeginScanGalvoCard(ref int Count);

        /// <summary>获取振镜卡信息</summary>
        /// <param name="CardNum">卡序号，从1开始</param>
        /// <param name="CardInfo">对应卡信息</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetScanGalvoInfo(int CardNum, ref TGalvoCardInfo CardInfo);

        /// <summary>设置振镜卡IP</summary>
        /// <param name="CardNum">卡序号，从1开始</param>
        /// <param name="IP">需要设置给卡的IP号</param>
        /// <param name="SubNet">设置对应子网掩码</param>
        /// <returns>阻塞式判断是否设置IP成功</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoCardIP(int CardNum, uint IP, uint SubNet);
        /// <summary>重置振镜卡IP(192.168.0.11)</summary>
        /// <param name="SN">振镜卡的SN号</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_ResetGalvoCardIP(Int64 SN);

        /// <summary>结束扫描振镜卡</summary>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EndScanGalvoCard();

        #endregion

        #region 立即指令(初始化)
        /// <summary>
        /// 初始化系统
        /// </summary>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_InitGalvoSystem();
        /// <summary>
        /// 释放振镜卡
        /// </summary>
        /// <param name="CardID"></param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_FreeGalvoCard(int CardID);
        /// <summary>释放振镜系统</summary>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_FreeGalvoSystem();

        /// <summary>初始化振镜卡</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_InitGalvoCard(Int64 SN, uint IP, ref int CardID);
        #endregion

        #region 立即指令(激光器)      
        /// <summary>
        /// 激光器MO port的使能状态
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LaserNum">激光器索引值，1-激光器1，2-激光器2</param>
        /// <param name="MOEnableState">MO使能状态，0-关闭，1-开启</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableLaserMO(int CardID, int LaserNum, int MOEnableState);

        /// <summary>
        /// 设置DA电压值
        /// </summary>
        /// <param name="CardID"> 振镜卡ID </param>
        /// <param name="DANum"> 模拟DA索引值，1-DA1，2-DA2 </param>
        /// <param name="DAValue"> 模拟功率输出值，0-10000，单位mV </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserDA(int CardID, int DANum, int DAValue);
        /// <summary>
        /// 设置数字功率值
        /// </summary>
        /// <param name="CardID"> 振镜卡ID </param>
        /// <param name="Digital"> 数字功率输出值，0-255 </param>
        /// <returns></returns>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserDigital(int CardID, int LaserNum, int Digital);
        /// <summary>
        /// 激光器AP光闸和PRR光闸的使能状态，注：光闸使能后对应口才能够输出信号
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LaserNum">激光器索引值，1-激光器1，2-激光器2</param>
        /// <param name="APShutterState">AP光闸的使能状态，0-不使能，1-使能</param>
        /// <param name="PRRShutterState">PRR光闸的使能状态，0-不使能，1-使能</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableLaserShutter(int CardID, int LaserNum, int APShutterState, int PRRShutterState);
        /// <summary>
        ///  设置AP频率和占空比 
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LaserNum">激光器索引值，1-激光器1，2-激光器2</param>
        /// <param name="Freq"></param>
        /// <param name="Ratio"></param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserAP(int CardID, int LaserNum, double Freq, double Ratio);

        /// <summary>
        /// 设置PRR频率和占空比
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="Freq">PRR频率, 单位[Hz], 范围[40,10000000]</param>
        /// <param name="Ratio">PRR占空比 [0,1.0]</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserPRR(int CardID, int LaserNum, double Freq, double Ratio);
        /// <summary>
        /// 开启激光器AP输出
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LasertNum">激光器索引值，1-激光器1，2-激光器2</param>
        /// <param name="APEmissionState">激光器AP状态，0-不开，1-开启，AP输出高电平</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableLaserAP(int CardID, int LasertNum, int APEmissionState);
        /// <summary>
        /// 激光器指示光使能
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LasertNum">激光器索引值，1-激光器1，2-激光器2</param>
        /// <param name="GuideLaserState">激光器指示光状态，0-不开指示光，1-开指示光</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableLaserGuide(int CardID, int LasertNum, int GuideLaserState);
        /// <summary>
        /// 激光器出光状态逻辑
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LasertNum">激光器索引值，1-激光器1，2-激光器2</param>
        /// <param name="APLogicState">激光器出光状态逻辑，0-高电平为出光状态，1-低电平为出光状态</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserAPLogic(int CardID, int LasertNum, int APLogicState);
        /// <summary>
        /// 激光器模式设置
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="LaserNum">激光器索引值，1-激光器1，2-激光器2 </param>
        /// <param name="LaserLevel">激光器模式，0-不出光，1-5V激光器，2-24V激光器</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserLevel(int CardID, int LaserNum, int LaserLevel);
        /// <summary>
        /// 启用 ScanPso 模式
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="Eable"></param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetScanPSOMode(int CardID, int Eable);
        



        #endregion

        #region 立即指令(IO)
        /// <summary>
        /// 设置IO输出电平
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="OutputValue">输出电平，0x00-全部拉低，0xff-全部拉高</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetOutputValue(int CardID, int OutputValue);
        /// <summary>
        /// 设置指定通道输出电平
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="PortNum">输出通道索引值，1-8</param>
        /// <param name="OutputValue">输出电平，0-拉低，1-拉高</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetOutputBit(int CardID, int PortNum, int OutputValue);
        /// <summary>
        /// 设置IO输出逻辑
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="OutputLogic">输出逻辑，0x00-全部常开，0xff-全部常闭</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetOutputLogicValue(int CardID, int OutputLogic);
        /// <summary>
        /// 设置指定通道输出逻辑
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="PortNum">输出通道索引值，1-8</param>
        /// <param name="OutputLogic">输出逻辑，0-常开，1-常闭</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetOutputLogicBit(int CardID, int PortNum, int OutputLogic);
        /// <summary>设置IO输入逻辑</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="InputLogic">输入逻辑，0x00-全部常开，0xff-全部常闭</param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetInputLogicValue(int CardID, int InputLogic);

        /// <summary>设置指定通道输入逻辑</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="PortNum">输入通道索引值，1-8</param>
        /// <param name="InputLogic">输入逻辑，0-常开，1-常闭</param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetInputLogicBit(int CardID, int PortNum, int InputLogic);

        /// <summary>配置指定输入口功能</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="PortNum">输入通道索引值，1-8</param>
        /// <param name="FunctionType">输入功能类型，0-通用输入，1-执行List，2-停止List，3-启用急停</param>
        /// <param name="FuncParam">功能参数，依据功能类型：对应执行List的Index</param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetInputFunc(int CardID, int PortNum, int FunctionType, int FuncParam);
        /// <summary>获取振镜卡输入口状态</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="InputState"> 输入口状态 </param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetInputState(int CardID, ref int InputState);
        /// <summary>获取振镜卡输出口状态</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="OutputState"> 输出口状态 </param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetOutputState(int CardID, ref int OutputState);

        #endregion

        #region 立即指令(振镜校正)
        /// <summary>
        /// 设置振镜机械参数
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="Focus">焦点距离, 场镜到加工面的距离,单位mm，大于0</param>
        /// <param name="Rad">振镜电机摆角度，单位rad，大于0</param>
        /// <param name="ProtocolNum">振镜协议 1代表XY2协议，2代表BC2协议 </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoMechParams(int CardID, int GalvoNum, double Focus, double Rad, uint ProtocolNum);
        /// <summary>
        /// 设置校正的比例因子
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="FileNum"> 校正文件序号，从1开始，1-8</param>
        /// <param name="Factor"> 比例因子 </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoFactor(int CardID, int GalvoNum, int FileNum, double Factor);
        /// <summary>
        /// 设置校正的振镜平台旋转角
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="FileNum"> 校正文件序号，从1开始，1-8</param>
        /// <param name="RotateAngle"> 振镜与平台坐标系夹角, 角度制, 逆时针为正, [-180, 180]< /param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoRotation(int CardID, int GalvoNum, int FileNum, double RotateAngle);
        /// <summary>
        /// 设置振镜坐标系
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="XYExChanged">交换XY轴，1:交换 0:不交换</param>
        /// <param name="XReversed">X轴反向，1:反向 0:不反向</param>
        /// <param name="YReversed">Y轴反向，1:反向 0:不反向</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoCoordinateSys(int CardID, int GalvoNum, int XYExChanged, int XReversed, int YReversed);

        /// <summary>
        /// 获取振镜机械参数
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="Focus">焦点距离, 场镜到加工面的距离,单位mm，大于0</param>
        /// <param name="Rad">振镜电机摆角度，单位rad，大于0</param>
        /// <param name="ProtocolNum">振镜协议 1代表XY2协议，2代表BC2协议 </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoMechParams(int CardID, int GalvoNum, ref double Focus, ref double Rad, ref uint ProtocolNum);
        /// <summary> 获取校正文件的比例因子 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="FileNum"> 校正文件序号，从1开始，1-8</param>
        /// <param name="Factor"> 比例因子 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoFactor(int CardID, int GalvoNum, int FileNum, ref double Factor);
        /// <summary> 读取旋转角度[角度] </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="FileNum"> 校正文件序号，从1开始，1-8</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoRotation(int CardID, int GalvoNum, int FileNum, ref double RotateAngle);
        /// <summary> 获取振镜校正x、y轴方向 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="XYExChanged">交换XY轴，1:交换 0:不交换</param>
        /// <param name="XReversed">X轴反向，1:反向 0:不反向</param>
        /// <param name="YReversed">Y轴反向，1:反向 0:不反向</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoCoordinateSys(int CardID, int GalvoNum, ref int XYExChanged, ref int XReversed, ref int YReversed);
        /// <summary> 设置启用Box校正 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        /// <param name="Enable"> 是否启用, 0:不启用, 1:启用 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableBoxCorrection(int CardID, int GalvoNum, int FileNum, int Enable);
        /// <summary> 设置Box校正参数 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        /// <param name="H1"> 边长 </param>
        /// <param name="H2"> 边长 </param>
        /// <param name="H3"> 边长 </param>
        /// <param name="W1"> 边长 </param>
        /// <param name="W2"> 边长 </param>
        /// <param name="W3"> 边长 </param>
        /// <param name="W"> 幅面尺寸 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetBoxCorrectionParams(int CardID, int GalvoNum, int FileNum, double H1, double H2, double H3, double W1, double W2, double W3, double W);
        /// <summary> 读取Box校正是否启用 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        /// <param name="Enable"> 是否启用, 0:不启用, 1:启用 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetBoxCorrectionEnabled(int CardID, int GalvoNum, int FileNum, ref int Enable);
        /// <summary> 读取Box校正参数 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        /// <param name="H1"> 边长 </param>
        /// <param name="H2"> 边长 </param>
        /// <param name="H3"> 边长 </param>
        /// <param name="W1"> 边长 </param>
        /// <param name="W2"> 边长 </param>
        /// <param name="W3"> 边长 </param>
        /// <param name="W"> 幅面尺寸 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetBoxCorrectionParams(int CardID, int GalvoNum, int FileNum, ref double H1, ref double H2, ref double H3, ref double W1, ref double W2, ref double W3, ref double W);
        /// <summary> 设置校正文件 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        /// <param name="FileName"> 校正文件名 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetCorrection(int CardID, int GalvoNum, int FileNum, string FileName);
        /// <summary> 选择生效的振镜校正文件序号 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SelectCorrectionNum(int CardID, int GalvoNum, int FileNum);
        /// <summary> 读取当前生效校正文件的索引 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetCurCorrectionNum(int CardID, int GalvoNum, ref int FileNum);
        /// <summary> 获取校正文件 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        /// <param name="FileName">校正文件名</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetCorrection(int CardID, int GalvoNum, int FileNum, string FileName);
        /// <summary> 清空多阶校正文件 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="FileNum"> 校正文件序号, [1,8] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_Clear2DCorrectionTable(int CardID, int GalvoNum, int FileNum);
        /// <summary>
        /// 振镜粗校正
        /// </summary>
        /// <param name="Order">校正阶数 >=3</param>
        /// <param name="Range">校正幅面 >=10</param>
        /// <param name="DataFileName">原始测量数据文件名</param>
        /// <param name="FileName">校正补偿数据文件名</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_ExecRoughCorrection(int Order, double Range, string DataFileName, string FileName);
        /// <summary>
        /// 振镜粗校正
        /// </summary>
        /// <param name="Order">校正阶数 >=3</param>
        /// <param name="Range">校正幅面 >=10</param>
        /// <param name="DataFileName">原始测量数据文件名</param>
        /// <param name="FileName">校正补偿数据文件名</param>
        /// <param name="LastFileName">上次校正补偿数据文件名</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_ExecFineCorrection(int Order, double Range, string DataFileName, string FileName, string LastFileName);
        /// <summary>
        /// 设置多阶校正文件
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="GalvoNum"></param>
        /// <param name="FileNum"></param>
        /// <param name="FileName">校正补偿文件名</param>
        /// <returns></returns>

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_Set2DCorrectionTable(int CardID, int GalvoNum, int FileNum, string FileName);
        /// <summary>
        /// 获取多阶校正文件
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="GalvoNum"></param>
        /// <param name="FileNum"></param>
        /// <param name="FileName">校正补偿文件名</param>
        /// <returns></returns>

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_Get2DCorrectionTable(int CardID, int GalvoNum, int FileNum, string FileName);
        /// <summary>
        /// 执行仿射 PT 校正
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="GalvoNum"></param>
        /// <param name="FileNum"></param>
        /// <param name="FileName"></param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_ExecAffinePTModelCorrection(int CardID, int GalvoNum, int FileNum, string FileName);



        #endregion

        #region 立即指令(List)
        /// <summary> 设置从List哪里开始加载后续指令 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="ListPos"> List位置 </param>
        /// <param name="ListNum"> List编号，目前只有1 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_LoadList(int CardID, int ListPos, int ListNum = 1);
        /// <summary> 设置从List哪里开始执行后续指令 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="ListPos"> List位置 </param>
        /// <param name="ListNum"> List编号，目前只有1 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_StartExecuteList(int CardID, int ListPos, int ListNum = 1);
        /// <summary> 停止执行List </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="ListNum"> List编号，目前只有1 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_StopList(int CardID, int ListNum = 1);
        /// <summary>
        /// 获取List的执行状态
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="ListState"> List状态， 0 空闲， 1 正在执行 </param>
        /// <param name="ListNum"> List编号，目前只有1 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetListState(int CardID, ref int ListState, int ListNum = 1);
        /// <summary>
        /// 获取上次加工时间
        /// </summary>
        /// <param name="CardID">CardID</param>
        /// <param name="WorkTime">加工时间</param>
        /// <param name="ListNum">List序号</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetListWorkTime(int CardID, int WorkTime, int ListNum=1);
        /// <summary>
        /// 获取 List 添加下一条指令的位置
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="ListPos"></param>
        /// <param name="ListNum"></param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetListPos(int CardID, ref int ListPos, int ListNum);
        #endregion

        #region 立即指令(编码器)

        /// <summary>
        /// 获取振镜卡编码器值
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="EncoderNum"> 编码器索引值，1，2 </param>
        /// <param name="EncoderValue"> 编码器值 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetEncoderValue(int CardID, int EncoderNum, ref Int64 EncoderValue);
        /// <summary>
        /// 获取振镜卡编码器速度
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="EncoderNum"> 编码器索引值：1，2 </param>
        /// <param name="EncoderSpeed"> 编码器速度，count/s </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetEncoderSpeed(int CardID, int EncoderNum, ref double EncoderSpeed);
        /// <summary>
        /// 设置编码器位置预测
        /// </summary>
        /// <param name="CardID">控制卡ID</param>
        /// <param name="EncoderNum">编码器索引值 1,2</param>
        /// <param name="SampleTime">采样时间</param>
        /// <param name="PredictTime">位置预测时间 单位us</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetEncoderPosPredicted(int CardID, int EncoderNum, int SampleTime, int PredictTime);
        /// <summary>
        /// 设置编码器模拟轴
        /// </summary>
        /// <param name="CardID">控制卡ID</param>
        /// <param name="EncoderNum">编码器索引值 1,2</param>
        /// <param name="Enable">是否启用模拟轴 0:不启用；1：启用</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableSimuEncoder(int CardID, int EncoderNum, int Enable);
        /// <summary>
        /// 获取编码器脉冲当量
        /// </summary>
        /// <param name="CardID">控制卡ID</param>
        /// <param name="EncoderNum">编码器索引值 1,2</param>
        /// <param name="EncoderResolution">编码器脉冲当量(默认值10000.0)</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetEncoderResolution(int CardID, int EncoderNum, ref double EncoderResolution);

        #endregion

        #region 立即指令(POF)
        /// <summary> 设置旋转飞打的圆心 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="X"> 旋转飞打的圆心X坐标 </param>
        /// <param name="Y"> 旋转飞打的圆心Y坐标 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetFlyRotCenter(int CardID, int GalvoNum, double X, double Y);
        #endregion

        #region 立即指令(螺距补偿)
        /// <summary> 螺距补偿相关: 使能螺距补偿 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="EncoderNum"> 编码器索引值，1，2 </param>
        /// <param name="En"> </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnablePitchConpensation(int CardID, int EncoderNum, int En);

        /// <summary> 螺距补偿相关: 设置螺距补偿表 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="EncoderNum"> 编码器索引值，1，2 </param>
        /// <param name="PointTotalCnt"> 螺距补偿表的总点数 </param>
        /// <param name="Delta"> 点间距（正负）单位mm </param>
        /// <param name="PosData"> 正向实测值数组,单位为 mm</param>
        /// <param name="NegData"> 反向实测值数组,单位为 mm</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetPitchConpensationTable(int CardID, int EncoderNum, int PointTotalCnt, double Delta, IntPtr PosData, IntPtr NegData);
        #endregion

        #region 立即指令(数据监控)
        /// <summary>
        /// 设置当前监控的配置参数
        /// </summary>
        /// <param name="CardID">控制卡 ID</param>
        /// <param name="ParamsArr">传入要监控的变量的编号</param>
        /// <param name="Count">设置要监控的变量总数</param>
        /// <param name="Period">当前监控的监控周期,周期为 10us</param>
        /// <param name="SamplingMode">采样方式</param>
        /// <param name="TermMode">结束方式</param>
        /// <param name="Signal">目标变量的索引</param>
        /// <param name="Signal1">比较参数 1，采样方式为小于，大于，等于，突变时生效</param>
        /// <param name="Signal2">比较参数 2，结束方式为小于，大于，等于，突变时生效</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetTriggerParams(int CardID, IntPtr ParamsArr, int Count, int Period, int SamplingMode, int TermMode, int Signal, double Signal1, double Signal2);
        /// <summary> 获取监控数据</summary>
        /// <param name="CardID"> 控制卡的ID</param>
        /// <param name="MonitorParamArr"> 数据存放的数组</param>
        /// <param name="DataCount"> 本次希望读取的数据周期数</param>
        /// <param name="StatusCode">真实数据个数 -1表示读取失败</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetWaveForm(int CardID, IntPtr MonitorParamArr, int DataCount, ref int StatusCode);
        /// <summary> 设置振镜监控控制字 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="ChannelNum"> 通道号, [1,4] </param>
        /// <param name="CmdX"> 振镜X轴控制字</param>
        /// <param name="CmdY"> 振镜Y轴控制字</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoMonitorCmd(int CardID, int GalvoNum, int ChannelNum, int CmdX, int CmdY);
        /// <summary> 获取振镜监控反馈值 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="ChannelNum"> 通道号, [1,4] </param>
        /// <param name="DataX"> 振镜X轴反馈</param>
        /// <param name="DataY"> 振镜Y轴反馈</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoMonitorData(int CardID, int GalvoNum, int ChannelNum, ref int DataX, ref int DataY);

        #endregion

        #region 立即指令(固件)
        /// <summary>获取固件版本</summary>
        /// <param name="CardID"> 控制卡的ID</param>
        /// <param name="VersionId"> 固件版本</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        //public static extern int BC2_GetFirmWareVer(int CardID, [MarshalAs(UnmanagedType.LPStr)] ref string VersionId);
        public static extern int BC2_GetFirmWareVer(int CardID, ref IntPtr VersionId);
        /// <summary>升级固件</summary>
        /// <param name="CardID"> 控制卡的ID</param>
        /// <param name="FileName"> 固件地址</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_UpgradeFirmWare(int CardID, string FileName);
        #endregion          

        #region 立即指令(报警)
        /// <summary>清除报警</summary>
        /// <param name="CardID"> 控制卡的ID</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_ClearErr(int CardID);
        /// <summary> 设置有效幅面报警阈值 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"> 振镜编号, [1,2] </param>
        /// <param name="XEffectiveWidth"> X轴有效幅面阈值[mm] </param>
        /// <param name="YEffectiveWidth"> Y轴有效幅面阈值[mm] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoEffectiveWidth(int CardID, int GalvoNum, double XEffectiveWidth, double YEffectiveWidth);

        /// <summary>
        /// 获取振镜卡报警值
        /// </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoCardErrcode"> 振镜卡报警值 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoCardErrcode(int CardID, ref int GalvoCardErrcode);
        /// <summary>
        /// 获取振镜卡报警信息
        /// </summary>
        ///<param name = "CardID" > 控制卡的ID </ param >
        ///< param name="ErrInfoNum">振镜卡报警信息编号[1,7] 1:TCP 2:参数 3:List 4:激光器 5:文件系统 6:振镜1 7:振镜2</param>
        ///<param name = "GalvoCardErrInfo" > 振镜卡报警信息 </ param >
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetGalvoCardErrInfo(int CardID, int ErrInfoNum, ref int GalvoCardErrInfo);
        /// <summary>
        /// 使能BC2反馈报警
        /// </summary>
        ///<param name = "CardID" > 控制卡的ID </ param >
        ///< param name="GalvoNum">振镜编号，[1,2]</param>
        ///<param name = "Enable" > 是否启用，0：不启用；1：启用 </ param >
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableBC2FeedBackAlarm(int CardID, int GalvoNum, int Enable);
        #endregion

        #region 立即指令(振镜）
        /// <summary> 修改一拖二加工的使能 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Enable"> 是否启用, 0:不启用, 1:启用 </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_EnableSecondGalvo(int CardID, int Enable);
        /// <summary>
        /// 振镜 Moveto 到指定位置
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="GalvoNum"></param>
        /// <param name="PosX">空移的 X 轴位置，单位mm</param>
        /// <param name="PosY">空移的 Y 轴位置，单位mm</param>
        /// <returns>1 表示通讯异常，-2 表示已经有一条 ControCmd 指令在执行，-3 表示正在加工执行  List 指令，-4 表示一拖二场景下，振镜参数传   入异常，-5 表示内部异常报警，</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GalvoMoveto(int CardID, int GalvoNum, double PosX, double PosY);
        /// <summary>
        /// 设置振镜 Moveto 速度
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="GalvoNum"></param>
        /// <param name="MovetoSpeed">振镜 moveto 速度，单位mm/s</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetJumpSpeed(int CardID, int GalvoNum, double MovetoSpeed);
        /// <summary>
        /// 振镜指令整体延时
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="GalvoCmdDelay"> 振镜整体延时，单位10us </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGalvoCmdDelay(int CardID, int GalvoCmdDelay);
        #endregion

        #region 缓存指令(振镜）

        /// <summary> 设置图纸旋转 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum"></param>
        /// <param name="RotateAngle"> 图纸旋转角度[-180 ~ 180][单位：°][逆时针为正]< /param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGraphicRotation_List(int CardID, int GalvoNum, double RotateAngle);
        /// <summary> 设置图元偏移 </summary>
        /// <param name="GalvoNum"></param>
        /// <param name="X">单位mm</param>
        /// <param name="Y">单位mm</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGraphicOffset_List(int CardID, int GalvoNum, double X, double Y);
        /// <summary> 设置图元缩放 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="GalvoNum">振镜头序号，从1开始，1-2</param>
        /// <param name="ScaleX">X方向缩放比例</param>
        /// <param name="ScaleY">Y方向缩放比例</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetGraphicScale_List(int CardID, int GalvoNum, double ScaleX, double ScaleY);
        /// <summary> 设置Mark速度 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="MarkSpeed">单位mm/s</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetMarkSpeed_List(int CardID, double MarkSpeed);
        /// <summary> 设置Jump速度 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="JumpSpeed">单位mm/s</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetJumpSpeed_List(int CardID, double JumpSpeed);

        /// <summary> 设置Jump延时、Mark延时、Corner延时 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="JumpDelay"> 单位10us</param>
        /// <param name="MarkEndDelay">单位10us</param>
        /// <param name="CornerDelay">单位10us</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetScannerDelay_List(int CardID, Int64 JumpDelay, Int64 MarkEndDelay, Int64 CornerDelay);
        /// <summary> 设置拐角延时模式、拐角延时内关光阈值</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Mode"> 拐角延时模式   0：直接延时  1：可变拐角延时</param>
        /// <param name="DelayEdge"> 拐角延时内关光阈值，单位10us</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetCornerDelayMode_List(int CardID, int Mode, Int64 DelayEdge);
        /// <summary>
        /// 设置直接拐角延时生效阈值
        /// </summary>
        /// <param name="CardID">控制卡 ID</param>
        /// <param name="CornerTheta">拐角生效需要的拐角阈值[单位：°]拐角需要大于等于这个角度才会生效拐角延时(仅限拐角延时模式为直接延时)</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetCornerDelayLimit_List(int CardID, double CornerTheta);
        /// <summary> 设置可变跳转延时参数</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="MinJumpDelay">最小跳转延时 单位10us </param>
        /// <param name="JumpLengthLimit"> 跳转延时长度阈值 单位mm</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetVarJumpDelayParam_List(int CardID, Int64 MinJumpDelay, double JumpLengthLimit);
        /// <summary> SkyWriting模式设定函数</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Mode"> </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetSkyWritingMode_List(int CardID, int Mode);
        /// <summary> SkyWriting渐入渐出参数设定</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="SkyWritingInDelay"> 单位10us</param>
        /// <param name="SkyWritingOutDelay"> 单位10us</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetSkyWritingTime_List(int CardID, double SkyWritingInDelay, double SkyWritingOutDelay);
        /// <summary> 激光器延时（带SkyWriting）设定函数</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Timelag"> 单位1ns</param>
        /// <param name="LaserOnShift"> 单位1ns</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetSkyWritingLaserDelay_List(int CardID, Int64 Timelag, Int64 LaserOnShift);
        /// <summary> 拐角SkyWriting生效阈值</summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="SkyWritingTheta"> [单位：°]</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetSkyWritingCornerLimit_List(int CardID, Double SkyWritingTheta);
        /// <summary> 设置激光器开、关光延时 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="LaserOnDelay">单位ns</param>
        /// <param name="LaserOffDelay">单位ns</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserDelay_List(int CardID, Int64 LaserOnDelay, Int64 LaserOffDelay);
        /// <summary> 加工一条直线 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="X">单位mm</param>
        /// <param name="Y">单位mm</param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_MarkLineAbs_List(int CardID, double X, double Y);
        /// <summary> 空移一条直线 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="X">单位mm</param>
        /// <param name="Y">单位mm</param>
        /// <returns>操作结果</returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_JumpLineAbs_List(int CardID, double X, double Y);

        /// <summary> 加工一段圆弧 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="X"> 圆心 </param>
        /// <param name="Y"> 圆心 </param>
        /// <param name="Angle"> 角度（单位°）[逆时针为正] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_MarkArcAbs_List(int CardID, double X, double Y, double Angle);

        /// <summary> 设置速度规划模式（速度优先：严格按照设置的速度运动，高速情况下拐角精度损失较大；图形优先：保证线端点指令，精度损失小，但可能会降速 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Mode"> 速度规划模式，1:速度优先（严格按照设置的速度运动，高速情况下拐角精度损失较大），0：图形优先(保证线端点指令，精度损失小，但可能会降速）;</param>

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetSplitMode_List(int CardID, int Mode);
        /// <summary>
        /// 设置 Wobble 抖动形状和抖动方向
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="WobbleMode">Wobble 抖动的图形形状 0 不开启； 1 正弦曲线； 2 横 8 ∞； 3 竖 8 ；4 圆或椭圆</param>
        /// <param name="WobbleDirection">Wobble 抖动的方向</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetWobbleMode_List(int CardID, int WobbleMode, int WobbleDirection);

        /// <summary>
        /// 设置 Wobble 抖动参数
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="WobbleAmp">平行于 Wobble 抖动方向的振幅，单位 mm对于正弦曲线，此参数不生效，sin 曲线的振幅由 WobbleAmpExt来生效</param>
        /// <param name="WobbleAmpExt">垂直于 Wobble 抖动方向的振幅，单位 mm</param>
        /// <param name="WobblePeriod">Wobble 的抖动周期，单位 s</param>
        /// <param name="WobblePhase0">Wobble 图形的初始相位，对应 0-2π</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetWobbleParam_List(int CardID, double WobbleAmp, double WobbleAmpExt, double WobblePeriod, double WobblePhase0);
        /// <summary>
        /// 设置 Wobble 偏移量
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="WobbleAmpOffset">Wobble 坐标系下，平行于 Wobble 抖动方向的图纸偏移量，单位 mm</param>
        /// <param name="WobbleAmpExtOffset">Wobble 坐标系下，垂直于 Wobble 抖动方向的图纸偏移量，单位 mm</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetWobbleOffset_List(int CardID, double WobbleAmpOffset, double WobbleAmpExtOffset);
        /// <summary>
        /// 设置加工延时
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="DelayTime">加工延时时间</param>
        [DllImport("GalvoApi2.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetDelay_List(int CardID, int DelayTime);
        #endregion

        /// <summary>
        /// 设置首尾补偿长度
        /// </summary>
        /// <param name="CardID">控制卡 ID</param>
        /// <param name="StartoffsetLength">首补偿长度：单位 mm, 沿加工方向偏移为正，否则为负</param>
        /// <param name="EndoffsetLength">尾补偿长度：单位 mm, 沿加工方向偏移为正，否则为负</param>
        /// <param name="Enable">是否使能首尾补偿 ：1开启， 0:关闭</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetMarkLengthOffset_List(int CardID, double StartoffsetLength, double EndoffsetLength, int Enable);

        #region 缓存指令(激光器)

        /// <summary>
        /// 设置DA电压值[List指令]
        /// </summary>
        /// <param name="CardID"> 振镜卡ID </param>
        /// <param name="DANum"> 模拟DA索引值，1-DA1，2-DA2 </param>
        /// <param name="DAValue"> 模拟功率输出值，0-10000，单位mV </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserDA_List(int CardID, int DANum, int DAValue);
        /// <summary>
        /// 设置数字功率值[List指令]
        /// </summary>
        /// <param name="CardID"> 振镜卡ID </param>
        /// <param name="Digital"> 数字功率输出值，0-255 </param>
        /// <returns></returns>

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserDigital_List(int CardID, int Digital);
        /// <summary>
        ///  设置AP频率和占空比[List指令] 
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="Freq"></param>
        /// <param name="Ratio"></param>
        /// <returns></returns>

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserAP_List(int CardID, double Freq, double Ratio);
        /// <summary>
        /// 设置PRR频率和占空比[List指令]
        /// </summary>
        /// <param name="CardID">振镜卡ID</param>
        /// <param name="Freq">PRR频率, 单位[Hz], 范围[40,10000000]</param>
        /// <param name="Ratio">PRR占空比 [0,1.0]</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserPRR_List(int CardID, double Freq, double Ratio);
        #endregion

        #region 缓存指令(List)
        /// <summary> 结束List的执行 </summary>
        /// <param name="CardID">控制卡的ID</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetEndOfList_List(int CardID);
        /// <summary> 设置指令循环执行起点 </summary>
        /// <param name="CardID">控制卡的ID</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_RepeatList_List(int CardID);
        /// <summary> 设置指令循环执行终点 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="RepeatTimes">循环次数 -1代表无限，0代表1次，N>0代表N次</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_UntilList_List(int CardID, int RepeatTimes);
        #endregion

        #region 缓存指令(POF)       
        /// <summary> 设置单轴x方向飞打模式 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Resolution"> x方向直线编码器的脉冲当量 </param>
        /// <param name="EncoderXNum"> x方向直线编码器的编号 [1,2] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetFlyX_List(int CardID, double Resolution, int EncoderXNum);
        /// <summary> 设置单轴y方向飞打模式 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Resolution"> x方向直线编码器的脉冲当量 </param>
        /// <param name="EncoderYNum"> y方向直线编码器的编号 [1,2] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetFlyY_List(int CardID, double Resolution, int EncoderYNum);
        /// <summary> 设置双轴飞打模式 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="ResolutionX,ResolutionY"> x方向,y方向直线编码器的脉冲当量 </param>
        /// <param name="EncoderXNum,EncoderYNum"> x方向,y方向直线编码器的编号 [1,2] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetFly2D_List(int CardID, double ResolutionX, double ResolutionY, int EncoderXNum, int EncoderYNum);

        /// <summary> 设置旋转飞打模式 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="Resolution"> 旋转编码器的每转脉冲数 </param>
        /// <param name="EncoderRotNum"> 旋转编码器器的编号 [1,2] </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetFlyRot_List(int CardID, double Resolution, int EncoderRotNum);

        /// <summary> 从设置的POF模式返回正常加工模式 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="X">  </param>
        /// <param name="Y">  </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_FlyReturn_List(int CardID, double X, double Y);

        #endregion

        #region 缓存指令(编码器)     

        /// <summary> 等待指定编码器到位 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="EncoderNum">  </param>
        /// <param name="TriggerMode"> 触发模式，-2为小于触发，+2为大于触发，-1为下降沿触发，+1为上升沿触发，=0为任意沿触发 </param>
        /// <param name="EncoderValue">  </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_WaitForEncoder_List(int CardID, int EncoderNum, int TriggerMode, Int64 EncoderValue);
        /// <summary> 设置编码器坐标 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="EncoderNum">  </param>
        /// <param name="SetType"> 0设置绝对位置,1设置相对偏移位置为零点 </param>
        /// <param name="EncPulse">  </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetEncoderPos_List(int CardID, int EncoderNum, int SetType, Int64 EncPulse);
        #endregion

        #region 缓存指令(IO)       
        /// <summary> 等待指定IO信号 </summary>
        /// <param name="CardID">控制卡的ID</param>
        /// <param name="InputPortNum">  </param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_WaitForIO_List(int CardID, int InputPortNum);
        #endregion

        #region 解析图纸

        /// <summary>
        /// 打开图纸并解析
        /// </summary>
        /// <param name="FilePath"> 图纸文件地址 </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_OpenAndParseFile(string FilePath);

        /// <summary>
        /// 删除解析的图纸信息
        /// </summary>
        /// <param name="FilePath"> 图纸文件地址 </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_DeleteFileInfo(string FilePath);

        /// <summary>
        /// 获取图纸中的图元数量
        /// </summary>
        /// <param name="FilePath"> 图纸文件地址 </param>
        /// <param name="ShapeCount"> 图纸中的图元数量</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetFileShapeCount(string FilePath, ref int ShapeCount);

        /// <summary>
        /// 获取图纸中的图层数量
        /// </summary>
        /// <param name="FilePath">  图纸文件名 </param>
        /// <param name="PenParamCount">  图纸中的图层数量 </param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetFilePenParamCount(string FilePath, ref int PenParamCount);

        /// <summary>
        /// 获取图纸中的图元信息
        /// </summary>
        /// <param name="FilePath">  图纸文件名</param>
        /// <param name="ShapeNum">  图纸中的图元编号</param>
        /// <param name="ShapeParam">  图元信息</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetFileShapeInfo(string FilePath, int ShapeNum, ref TShapeParam ShapeParam);

        /// <summary>
        /// 获取图纸中的图层信息
        /// </summary>
        /// <param name="FilePath">  图纸文件名</param>
        /// <param name="ShapeNum">  图纸中的图层编号</param>
        /// <param name="ShapeParam">  图层信息</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_GetFilePenParams(string FilePath, int PenNum, ref TPenParam PenParam);

        #endregion

        #region 其他

        ///<summary>是否使能Debug模式</summary>
        ///<param name="Enable">使能:不开启，1: 开启</param>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_DebugEnable(int Enable);

        #endregion

        #region 接口返回值

        public const int ERRCODE_SUCCESS = 0;                     //执行成功
        public const int ERRCODE_PARAMERROR = 1;                  //参数错误
        public const int ERRCODE_REPEATCREATE = 2;                //重复创建
        public const int ERRCODE_EXECUTER_FAILED = 3;             //执行失败
        public const int ERRCODE_GETRESOURCESFAILED = 4;          //获取资源失败
        public const int ERRCODE_STARTMARKFAILED = 5;             //开始标刻失败

        public const int ERRCODE_FILENOTFOUND = 6;                //文件名找不到
        public const int ERRCODE_FILEOPENFAILED = 7;              //文件打开失败
        public const int ERRCODE_INVALIDTECHPARAM = 8;            //无效的工艺参数

        public const int ERRCODE_CONNECTFAILED = 51;              //连接失败
        public const int ERRCODE_DISCONNECTERROR = 52;            //断开连接异常

        public const int ERRCODE_SYSTEMNOTINSTALL = 101;          //系统没有初始化

        public const int ERRCODE_CORRECTIONDATAINVALID = 201;     //振镜校正原始数据与理论值偏差过大
        public const int ERRCODE_PARAM_MIN_ORDER = 202;           //振镜校正阶数小于3
        public const int ERRCODE_PARAM_MIN_SIDELENGTH = 203;      //振镜校正幅面小于10
        public const int ERRCODE_PARAM_POINTCNT = 204;            //振镜校正原始数据的点位数不等于阶数的平方
        public const int ERRCODE_CALIFAILED = 205;                //校正点位计算失败
        public const int ERRCODE_PARAM_MIN_FACTOR = 206;          //比例因子小于等于0     
        public const int ERRCODE_PARAM_MIN_BOX = 207;            //桶形数据小于等于0

        public const int ERRCODE_TRIGGERERROR = 301;              //数据监控错误
        public const int ERRCODE_TRIGGERCYCLE = 302;              //循环采样中
        public const int ERRCODE_TRIGGERPAUSE = 303;              //存满，暂停中
        public const int ERRCODE_TRIGGERWAIT = 304;               //等待触发状态
        public const int ERRCODE_TRIGGERING = 305;                //正在采样
        public const int ERRCODE_TRIGGERPARAMSETFAIL = 306;       //参数设置失败
        #endregion
        // 常用变量、结构体定义
        #region 常用变量、结构体

        public struct TGalvoCardInfo
        {
            public Int64 SerialNum;
            public uint CardIP;
            public uint CardSubNet;
            public uint LocalIp;
            public uint LocalSubNet;
            public uint CardInfo;
            public TGalvoCardInfo(Int64 SerialNum, uint CardIP, uint CardSubNet, uint LocalIp, uint LocalSubNet, uint CardInfo)
            {
                this.SerialNum = SerialNum;     //SN号
                this.CardIP = CardIP;           //IP地址
                this.CardSubNet = CardSubNet;   //子网掩码
                this.LocalIp = LocalIp;         //主机连接网口IP
                this.LocalSubNet = LocalSubNet; //主机连接网口子网掩码
                this.CardInfo = CardInfo;       //卡信息

            }

        }

        public static int ErrCode;
        public static string[] Errs = { "TCP_Err", "Param_Err", "List_Err", "Laser_Err", "Yaffs_Err", "SudStop_Err", "Galvo1_Err", "Galvo2_Err" };

        public struct TGalvoPt2d
        {
            public double X;
            public double Y;
            public TGalvoPt2d(double a, double b)
            {
                this.X = a;
                this.Y = b;
            }
        }

        //public unsafe struct TShapeParam
        //{
        //    public int ShapeType;
        //    public double DelayIn;
        //    public double DelayOut;
        //    public double Angle;
        //    public double LaserOnDelay;
        //    public double LaserOffDelay;
        //    public int WobbleMode;
        //    public int WobbleDirection;
        //    public double WobbleAmp;
        //    public double WobbleAmpExt;
        //    public double WobblePeriod;
        //    public double WobblePhase0;
        //    public double WobbleAmpOffset;
        //    public double WobbleAmpExtOffset;
        //    public int PenNum;
        //    public TGalvoPt2d ArcStart;
        //    public TGalvoPt2d ArcCenter;
        //    public double ArcAngle;
        //    public int PtCount;
        //    public TGalvoPt2d* PointArr;
        //    public TGalvoPt2d Pos;
        //    public TGalvoPt2d PtStart;
        //    public TGalvoPt2d PtEnd;
        //    public double ExtendIn;
        //    public double ExtendOut;
        //    // public int PLCType;
        //    // public int PLCParam1;
        //    // public int PLCParam2;
        //    // public int PLCParam3;

        //    public TShapeParam(int shapeType, double delayIn, double delayOut, double angle, double laserOnDelay, double laserOffDelay, int wobbleMode, int wobbleDirection, double wobbleAmp,
        //         double wobbleAmpExt, double wobblePeriod, double wobblePhase0, double wobbleAmpOffset, double wobbleAmpExtOffset, int penNum, TGalvoPt2d arcStart, TGalvoPt2d arcCenter, double arcAngle, int ptCount,
        //         TGalvoPt2d* pointArr, TGalvoPt2d pos, TGalvoPt2d ptStart, TGalvoPt2d ptEnd, double extendIn, double extendOut)//, int pLCType, int pLCParam1, int pLCParam2, int pPLCParam3
        //    {
        //        ShapeType = shapeType;
        //        DelayIn = delayIn;
        //        DelayOut = delayOut;
        //        Angle = angle;
        //        LaserOnDelay = laserOnDelay;
        //        LaserOffDelay = laserOffDelay;
        //        WobbleMode = wobbleMode;
        //        WobbleDirection = wobbleDirection;
        //        WobbleAmp = wobbleAmp;
        //        WobbleAmpExt = wobbleAmpExt;
        //        WobblePeriod = wobblePeriod;
        //        WobblePhase0 = wobblePhase0;
        //        WobbleAmpOffset = wobbleAmpOffset;
        //        WobbleAmpExtOffset = wobbleAmpExtOffset;
        //        PenNum = penNum;
        //        ArcStart = arcStart;
        //        ArcCenter = arcCenter;
        //        ArcAngle = arcAngle;
        //        PtCount = ptCount;
        //        PointArr = pointArr;
        //        Pos = pos;
        //        PtStart = ptStart;
        //        PtEnd = ptEnd;
        //        ExtendIn = extendIn;
        //        ExtendOut = extendOut;
        //        //PLCType = pLCType;
        //        // PLCParam1 = pLCParam1;
        //        //PLCParam2 = pLCParam2;
        //        // PLCParam3 = pPLCParam3;
        //    }
        //}


        public struct TShapeParam
        {
            public int ShapeType;
            public double DelayIn;
            public double DelayOut;
            public double Angle;
            public double LaserOnDelay;
            public double LaserOffDelay;
            public int WobbleMode;
            public int WobbleDirection;
            public double WobbleAmp;
            public double WobbleAmpExt;
            public double WobblePeriod;
            public double WobblePhase0;
            public double WobbleAmpOffset;
            public double WobbleAmpExtOffset;
            public int PenNum;
            public TGalvoPt2d ArcStart;
            public TGalvoPt2d ArcCenter;
            public double ArcAngle;
            public int PtCount;
            public IntPtr PointArr;
            public TGalvoPt2d Pos;
            public TGalvoPt2d PtStart;
            public TGalvoPt2d PtEnd;
            public double ExtendIn;
            public double ExtendOut;
            // public int PLCType;
            // public int PLCParam1;
            // public int PLCParam2;
            // public int PLCParam3;

            public TShapeParam(int shapeType, double delayIn, double delayOut, double angle, double laserOnDelay, double laserOffDelay, int wobbleMode, int wobbleDirection, double wobbleAmp,
                 double wobbleAmpExt, double wobblePeriod, double wobblePhase0, double wobbleAmpOffset, double wobbleAmpExtOffset, int penNum, TGalvoPt2d arcStart, TGalvoPt2d arcCenter, double arcAngle, int ptCount,
                 IntPtr pointArr, TGalvoPt2d pos, TGalvoPt2d ptStart, TGalvoPt2d ptEnd, double extendIn, double extendOut)//, int pLCType, int pLCParam1, int pLCParam2, int pPLCParam3
            {
                ShapeType = shapeType;
                DelayIn = delayIn;
                DelayOut = delayOut;
                Angle = angle;
                LaserOnDelay = laserOnDelay;
                LaserOffDelay = laserOffDelay;
                WobbleMode = wobbleMode;
                WobbleDirection = wobbleDirection;
                WobbleAmp = wobbleAmp;
                WobbleAmpExt = wobbleAmpExt;
                WobblePeriod = wobblePeriod;
                WobblePhase0 = wobblePhase0;
                WobbleAmpOffset = wobbleAmpOffset;
                WobbleAmpExtOffset = wobbleAmpExtOffset;
                PenNum = penNum;
                ArcStart = arcStart;
                ArcCenter = arcCenter;
                ArcAngle = arcAngle;
                PtCount = ptCount;
                PointArr = pointArr;
                Pos = pos;
                PtStart = ptStart;
                PtEnd = ptEnd;
                ExtendIn = extendIn;
                ExtendOut = extendOut;
                //PLCType = pLCType;
                // PLCParam1 = pLCParam1;
                //PLCParam2 = pLCParam2;
                // PLCParam3 = pPLCParam3;
            }
        }


        public struct TPenParam
        {
            public int PenNum;
            public int ProcessCount;
            //笔号
            public double MarkSpeed;
            public double JumpSpeed;
            public Int64 LaserOnDelay;
            public Int64 LaserOffDelay;
            public int MarkEndDelay;
            public int JumpEndDelay;
            public int MinJumpDelay;
            public double JumpLengthLimit;
            public int CornerDelay;
            public int CornerDelayMode;
            public int CornerDelayEdge;

            public double Power;
            public double PwmFrequency;
            public double PwmDutyRatio;
            public double PrrFrequency;
            public double PrrDutyRatio;

            public int SecondLaserParamEnable;
            public double OuterPower;
            public int PulseWidth;
            public int RaycusPulseWidth;

            public TPenParam(int penNum, int processCount, double markSpeed, double jumpSpeed, Int64 laserOnDelay, Int64 laserOffDelay, int markEndDelay,
                int jumpEndDelay, int minJumpDelay, double jumpLengthLimit, int cornerDelay, int cornerDelayMode, int cornerDelayEdge, double power,
                double pwmFrequency, double pwmDutyRatio, double prrFrequency, double prrDutyRatio, int secondLaserParamEnable, double outerPower, int pulseWidth, int raycusPulseWidth)
            {
                PenNum = penNum;
                ProcessCount = processCount;
                MarkSpeed = markSpeed;
                JumpSpeed = jumpSpeed;
                LaserOnDelay = laserOnDelay;
                LaserOffDelay = laserOffDelay;
                MarkEndDelay = markEndDelay;
                JumpEndDelay = jumpEndDelay;
                MinJumpDelay = minJumpDelay;
                JumpLengthLimit = jumpLengthLimit;
                CornerDelay = cornerDelay;
                CornerDelayMode = cornerDelayMode;
                CornerDelayEdge = cornerDelayEdge;
                Power = power;
                PwmFrequency = pwmFrequency;
                PwmDutyRatio = pwmDutyRatio;
                PrrFrequency = prrFrequency;
                PrrDutyRatio = prrDutyRatio;
                SecondLaserParamEnable = secondLaserParamEnable;
                OuterPower = outerPower;
                PulseWidth = pulseWidth;
                RaycusPulseWidth = raycusPulseWidth;
            }

        }
        #endregion

        #region 配置导入
        /// <summary>
        /// 导入单张卡配置
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="FileName">单张卡配置文件，文件由平台配置工具内卡配置迁移导出</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_ImportCardConfig(int CardID, string FileName);
        /// <summary>
        /// 设置激光器功率，导入配置文件后
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="PowerValue">功率百分比</param>
        /// <param name="PowerValue2">第二路 DA 功率百分比，只有内外环激光器生效</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserPower_Import(int CardID, int PowerValue, int PowerValue2);
        /// <summary>
        /// 设置激光器频率占空比，导入配置文件后
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="Freq">频率, 单位[Hz][100,10000000]</param>
        /// <param name="Ratio">占空比0,1.0]</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserFreqAndRatio_Import(int CardID, double Freq, double Ratio);
        /// <summary>
        /// 缓存指令 设置激光器功率，导入配置文件后
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="PowerValue">功率百分比</param>
        /// <param name="PowerValue2">第二路 DA 功率百分比，只有内外环激光器生效</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserPower_Import_List(int CardID, int PowerValue, int PowerValue2);
        /// <summary>
        /// 缓存指令 设置激光器频率占空比，导入配置文件后
        /// </summary>
        /// <param name="CardID"></param>
        /// <param name="Freq">频率, 单位[Hz][100,10000000]</param>
        /// <param name="Ratio">占空比0,1.0]</param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetLaserFreqAndRatio_Import_List(int CardID, double Freq, double Ratio);
        /// <summary>
        /// 设置飞打参数，导入配置文件后
        /// </summary>
        /// <param name="CardID"></param>
        /// <returns></returns>
        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        public static extern int BC2_SetPOFConfig_Import_List(int CardID);
        #endregion

        // 自定义函数
        #region 自定义函数     
        public static int InitSystem(Int64 SerialNum, uint CardIP)
        {
            int CardID = -1;
            
            BC2_InitGalvoCard(SerialNum, CardIP, ref CardID);

            return CardID;
        }

        public static TGalvoCardInfo[] GetAllCardInfo(ref int Count)
        {
            int ErrCode; // 用于获取各个函数的执行状态代码
            int flag = 0;

            ErrCode = BC2_BeginScanGalvoCard(ref Count);
            //Assert.AreEqual(ErrCode, 0);
            /*
           while (Count == 0)
           {
               ErrCode = BC2_EndScanGalvoCard();
               MyAssert.AreEqual(ErrCode, 0);
               if (flag == 0)
               {
                   Mail.SendEmail("Count==0", "寄");
                   flag = 1;
               }


               ErrCode = BC2_BeginScanGalvoCard(ref Count);
               MyAssert.AreEqual(ErrCode, 0);
               FileTool.WriteToFile("+1", "ScanFail");
           }
           */

            if(Count != 0)
            {
                TGalvoCardInfo[] AllCardInfo = new TGalvoCardInfo[Count];
                for(int i = 0; i < Count; i++)
                {
                    ErrCode = BC2_GetScanGalvoInfo(i + 1, ref AllCardInfo[i]);
                    //MyAssert.AreEqual(ErrCode, 0);
                    ShowCardInfo(i + 1, AllCardInfo[i]);

                }

                ErrCode = BC2_EndScanGalvoCard();
                //MyAssert.AreEqual(ErrCode, 0);
                return AllCardInfo;
            }
            else
            {
                throw new Exception("未扫描到振镜卡");
            }

        }
        public static void ShowCardInfo(int Card, TGalvoCardInfo CardInfo)
        {
            Console.WriteLine("Card " + Card + "：\tSN：" + CardInfo.SerialNum + "\tCardInfo：" + CardInfo.CardInfo);
            Console.WriteLine($"\t本机IP：{IPIntToString(CardInfo.LocalIp),-15} 子网掩码：{IPIntToString(CardInfo.LocalSubNet),-15}");
            Console.WriteLine($"\t板卡IP：{IPIntToString(CardInfo.CardIP),-15} 子网掩码：{IPIntToString(CardInfo.CardSubNet),-15}");
            Console.WriteLine("………………………………………………………………………………");
        }
        public static string DecimalToBinaryString(int number)
        {
            // 将十进制数转换为二进制字符串
            string binaryStr = Convert.ToString(number, 2);

            // 逆序遍历二进制字符串，每隔四位插入一个空格
            string result = "";
            int count = 0;
            for(int i = binaryStr.Length - 1; i >= 0; i--)
            {
                if(count == 4)
                {
                    result = " " + result;
                    count = 0;
                }
                result = binaryStr[i] + result;
                count++;
            }

            return result;
        }
        public static int WaitOver(int CardID)
        {
            int ListState = 1;
            int GalvoCardErrCode = -1;
            int Times = 0;
            while(ListState == 1)
            {
                //Times += 1;
                //Console.WriteLine(Times + "次");
                ErrCode = BC2_GetListState(CardID, ref ListState);
                if(ErrCode != 0)
                {
                    Console.WriteLine("GetListState执行失败，ErrCode:" + ErrCode);
                    return 1;
                }


                ErrCode = BC2_GetGalvoCardErrcode(CardID, ref GalvoCardErrCode);
                if(ErrCode != 0)
                {
                    Console.WriteLine("GetGalvoCardErrcode执行失败，ErrCode:" + ErrCode);
                    return 2;
                }
                if(GalvoCardErrCode != 0)
                {
                    int Errinfo = -1;

                    Console.WriteLine("存在报警，GalvoCardErrCode：" + GalvoCardErrCode);
                    Console.WriteLine("报警类型：");
                    for(int i = 7; i > -1; i--)
                    {
                        if(Convert.ToString(GalvoCardErrCode, 2).PadLeft(8, '0')[i] == '1')
                        {
                            Console.Write(" " + Errs[7 - i]);
                            BC2_GetGalvoCardErrInfo(CardID, 8 - i, ref Errinfo);
                            Console.WriteLine("   ErrInfo：" + Errinfo + "=" + DecimalToBinaryString(Errinfo));
                        }
                        if(Convert.ToString(Errinfo, 2).PadLeft(8, '0')[i] == '1')
                        {

                        }
                    }
                    return 3;
                }


                Thread.Sleep(1);
            }

            return 0;

        }
        public static void GetEncoderValues(int CardID)
        {
            Int64[] Value = new Int64[2];
            BC2_GetEncoderValue(CardID, 1, ref Value[0]);
            BC2_GetEncoderValue(CardID, 2, ref Value[1]);
            Console.WriteLine("编码器1：" + Value[0] + " 编码器2：" + Value[1]);
        }
        /// <summary>
        /// IP字符串转化为数字
        /// </summary>
        /// <param name="ip">IP地址的字符串</param>
        /// <returns></returns>
        public static uint IPStringToInt(string ip)
        {
            IPAddress ipAddress = IPAddress.Parse(ip);
            byte[] bytes = ipAddress.GetAddressBytes();
            uint ipAddressInt = BitConverter.ToUInt32(bytes, 0);
            return ipAddressInt;
        }

        /// <summary>
        /// 将数字IP转换为字符串IP
        /// </summary>
        /// <param name="ip">int型数字的IP地址</param>
        /// <returns></returns>
        public static string IPIntToString(uint ip)
        {
            byte[] bytes = BitConverter.GetBytes(ip);
            IPAddress ipString = new IPAddress(bytes);
            return ipString.ToString();
        }


        /// <summary>
        /// 获取Dll版本
        /// </summary>
        /// <returns></returns>
        public static string GetDllVer()
        {
            string Ver = "";
            System.IO.FileInfo fileInfo = null;
            try
            {
                fileInfo = new System.IO.FileInfo(DllPath);
                Ver = fileInfo.LastWriteTime.ToString();
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
                // 其他处理异常的代码
            }

            if(fileInfo != null && fileInfo.Exists)
            {
                System.Diagnostics.FileVersionInfo info = System.Diagnostics.FileVersionInfo.GetVersionInfo(DllPath);
                Ver = info.FileVersion + " " + Ver;
            }
            else
            {
                Console.WriteLine("指定的文件路径不正确!");
            }
            Console.WriteLine($"Dll版本：{Ver}");
            Console.WriteLine($"………………………………………………………………………………");
            return Ver;

        }

        #endregion

        /// <summary>
        /// 输入卡ID, 打印当前报警信息
        /// </summary>
        /// <param name="CardID"></param>
        public static void GetErr(int CardID)
        {
            void WriteErrInfo(int Num, int Info)
            {
                switch (Num)
                {
                    case 0:
                        Console.Write("TCP报警：");
                        switch (Info)
                        {
                            case 0:
                                Console.Write("接受数据\n");
                                break;
                            case 1:
                                Console.Write("发送数据满\n");
                                break;
                            case 2:
                                Console.Write("丢帧报警\n");
                                break;
                            case 4:
                                Console.Write("心跳包报警\n");
                                break;
                        }
                        break;
                    case 1:
                        Console.Write("参数保存报警：");
                        switch (Info)
                        {
                            case 0:
                                Console.WriteLine("初始化参数超过最大数量\n");
                                break;
                            case 1:
                                Console.WriteLine("读取参数超过最大数量\n");
                                break;
                            case 2:
                                Console.WriteLine("CRC校验错误\n");
                                break;
                        }
                        break;
                    case 2:
                        Console.Write("指令队列报警：");
                        switch (Info)
                        {
                            case 0:
                                Console.Write("List队列满\n");
                                break;
                            case 1:
                                Console.Write("List队列空\n");
                                break;
                            case 2:
                                Console.Write("开关光规划异常\n");
                                break;
                            case 3:
                                Console.Write("List执行错误报警\n");
                                break;
                        }
                        break;

                    case 3:
                        Console.Write("激光器报警：");
                        switch (Info)
                        {
                            case 0:
                                Console.Write("开光队列满\n");
                                break;
                        }
                        break;
                    case 4:
                        Console.Write("文件系统报警：");
                        switch (Info)
                        {
                            case 0:
                                Console.Write("数据丢失\n");
                                break;
                            case 1:
                                Console.Write("文件系统挂载错误\n");
                                break;
                            case 2:
                                Console.Write("打开文件错误\n");
                                break;
                            case 4:
                                Console.Write("写入数据错误\n");
                                break;
                            case 5:
                                Console.Write("读取数据错误\n");
                                break;
                        }

                        break;
                    case 5:
                        Console.Write("急停报警：");
                        Console.Write("报警信息为：" + Info);
                        Console.WriteLine(" 正在报警的端口号为：");
                        Console.Write(Convert.ToString(Info, 2).PadLeft(8, '0') + "\n");
                        Thread.Sleep(1);
                        break;

                    case 6:
                    case 7:
                        Console.Write("振镜头 " + (Num - 5) + " 报警：");
                        switch (Info)
                        {
                            case (0):
                                Console.Write("物理指令状态机报警\n");
                                break;
                            case (1):
                                Console.Write("物理指令执行报警\n");
                                break;
                            case (2):
                                Console.Write("BC2反馈断连报警\n");
                                break;
                            case (16):
                                Console.Write("X正方向超有效幅面\n");
                                break;
                            case (17):
                                Console.Write("X负方向超有效幅面\n");
                                break;
                            case (18):
                                Console.Write("Y正方向超有效幅面\n");
                                break;
                            case (19):
                                Console.Write("Y负方向超有效幅面\n");
                                break;
                            case (20):
                                Console.Write("X正方向超物理幅面\n");
                                break;
                            case (21):
                                Console.Write("X负方向超物理幅面\n");
                                break;
                            case (22):
                                Console.Write("Y正方向超物理幅面\n");
                                break;
                            case (23):
                                Console.Write("Y负方向超物理幅面\n");
                                break;
                            case (24):
                                Console.Write("振镜Y轴电机位置偏差过大\n");
                                break;
                            case (25):
                                Console.Write("振镜X轴电机位置偏差过大\n");
                                break;
                            case (26):
                                Console.Write("振镜温度异常\n");
                                break;
                            case (27):
                                Console.Write("振镜电压异常\n");
                                break;
                        }
                        break;
                    default:
                        Console.Write("获取值异常,异常ErrInfo： " + Num + "\n");
                        break;
                }
            }

            int Errcode = 0;
            int Errinfo = 0;
            BC2_GetGalvoCardErrcode(CardID, ref Errcode);
            if (Errcode != 0)
            {
                Console.WriteLine("当前存在报警、报警码为：" + Errcode);
                //获取报警码，判断不为1的输出
                for (int i = 7; i > -1; i--)
                {
                    if (Convert.ToString(Errcode, 2).PadLeft(8, '0')[i] == '1')
                    {
                        BC2_GetGalvoCardErrInfo(CardID, 8 - i, ref Errinfo);
                        Console.WriteLine("bit" + (7 - i) + " 报警信息为：" + Errinfo);
                        //转为二进制
                        string binaryStr = Convert.ToString(Errinfo, 2);
                        for (int j = binaryStr.Length - 1; j >= 0; j--)
                        {
                            if (binaryStr[j] == '1')
                            {
                                if (7 - i == 5)
                                {
                                    Console.Write("急停报警：");
                                    Console.Write("报警信息为：" + Errinfo);
                                    Console.WriteLine(" 正在报警的端口号为：");
                                    Console.Write(Convert.ToString(Errinfo, 2).PadLeft(8, '0') + "\n");
                                    Thread.Sleep(1);
                                }
                                else
                                {
                                    WriteErrInfo(7 - i, binaryStr.Length - j - 1);
                                }

                            }
                        }
                    }
                    else
                    //获取一下异常情况
                    {
                        BC2_GetGalvoCardErrInfo(CardID, 8 - i, ref Errinfo);
                        if (Errinfo != 0)
                        {
                            Console.WriteLine("!!! 没有报警码但有报警信息：" + "bit" + (7 - i) + "报警信息" + Errinfo);
                            //Assert.AreEqual(0, Errinfo);
                        }

                    }
                }
            }
            else
            {
                Console.WriteLine("当前没报警哦!");
            }
        }


        /// <summary>
        /// 将内容写到文件内
        /// </summary>
        /// <param name="data"></param>
        /// <param name="fileName"></param>
        /// <param name="enter"></param>
        /// <param name="writedate">是否写入时间,默认写</param>
        public static void WriteData(string data, string fileName, int enter = 1,int writeTime = 1)
        {
            //将数据添加到文件内
            string date;
            string fName;

            date = DateTime.Now.ToString("yyyy_MM_dd");
            fName = fileName + date + ".txt";
            if (writeTime != 1)
            {
                data = DateTime.Now.ToString("G") + " " + data;   
            }
            string directoryPath = Path.GetDirectoryName(fName);

            // 检查文件夹是否存在，如果不存在则创建文件夹
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // 如果文件不存在，则创建文件
            if (!File.Exists(fName))
            {
                File.Create(fName).Dispose(); // 创建文件并关闭文件流
            }



            using (System.IO.StreamWriter file = new StreamWriter(fName, true))
            {
                if (enter == 1)
                {
                    file.WriteLine(data);
                }
                else
                {
                    file.Write(data);
                }

                file.Close();
            }

        }
        
        public static string GetNum(string HandleString, string KeyWord)
        {
            string ResultString = "-9999";
            {
                char[] StringLineArray = HandleString.ToCharArray();
                char[] KeyWordArray = KeyWord.ToCharArray();
                char[] ResultArray;

                int MainIndex = 0;
                int ViceIndex = 0;
                while (MainIndex < StringLineArray.Length && ViceIndex < KeyWordArray.Length)
                {
                    if (StringLineArray[MainIndex] == KeyWordArray[ViceIndex])
                    { // 当两个字符相同，就比较下一个
                        MainIndex++;
                        ViceIndex++;
                    }
                    else
                    {
                        MainIndex = MainIndex - ViceIndex + 1; // 一旦不匹配，MainIndex后退
                        ViceIndex = 0; // ViceIndex归0
                    }

                }

                if (ViceIndex == KeyWordArray.Length)
                {

                    int keylength = 1;
                    while (StringLineArray[MainIndex - ViceIndex + KeyWordArray.Length + keylength] != '}' && StringLineArray[MainIndex - ViceIndex + KeyWordArray.Length + keylength] != ',' && StringLineArray[MainIndex - ViceIndex + KeyWordArray.Length + keylength] != ' ')
                    {
                        keylength += 1;

                    }
                    ResultArray = new char[keylength];
                    Array.ConstrainedCopy(StringLineArray, (MainIndex - ViceIndex + KeyWordArray.Length)
                        , ResultArray, 0, keylength);
                    ResultString = string.Join("", ResultArray);

                }
            }

            return ResultString;
        }

        /// <summary>
        /// 发送解析好的图纸，进行缓存
        /// </summary>
        /// <param name="CardID">卡ID</param>
        /// <param name="file">已解析的图纸文件</param>
        /// <param name="PowerMode">1为数字功率,2为模拟功率 最大4V,3为模拟功率 最大5V,4为模拟功率 最大10V</param>
        /// <param name="LaserMode">1为PRR激光器,2为PWM激光器</param>
        public static int SendPicListCommand(int CardID, string file, int PowerMode = 1, int LaserMode = 1)
        {
            //1.脉宽值提前写入，不允许加工中切换
            //2.发skywriting的参数
            //  第一个图元的图层参数,全部写入,考虑是PWM / PRR、内外环功率、
            //  存一个图层参数的变量,如果变化了就再发一次
            //3.距离小于10^-6认为是Mark
            int ErrCode;
            int shapeCount = 0;
            int firstJump = 0;
            TShapeParam shapeParam = new TShapeParam();
            TPenParam penParam = new TPenParam();
            TPenParam lastPenParam = new TPenParam();

            TGalvoPt2d LastPosition = new TGalvoPt2d();
           
            IntPtr ptr = shapeParam.PointArr;
            int length = shapeParam.PtCount;
            //获取图元数量、图层信息进行解析
            ErrCode = BC2_GetFileShapeCount(file, ref shapeCount);
            if(ErrCode != 0) { return 1; }

            // 更新变化的图层函数

            void UpdateChangedPenParams(TPenParam lastPenParam, TPenParam currentPenParam)
            {
                if (lastPenParam.MarkSpeed != currentPenParam.MarkSpeed)
                {
                    BC2_SetMarkSpeed_List(CardID, currentPenParam.MarkSpeed);
                }

                if (lastPenParam.JumpSpeed != currentPenParam.JumpSpeed)
                {
                    BC2_SetJumpSpeed_List(CardID, currentPenParam.JumpSpeed);
                }

                if (lastPenParam.LaserOnDelay != currentPenParam.LaserOnDelay || lastPenParam.LaserOffDelay != currentPenParam.LaserOffDelay)
                {
                    BC2_SetLaserDelay_List(CardID, currentPenParam.LaserOnDelay, currentPenParam.LaserOffDelay);
                }


                if (lastPenParam.MarkEndDelay != currentPenParam.MarkEndDelay || lastPenParam.JumpEndDelay != currentPenParam.JumpEndDelay || lastPenParam.CornerDelay != currentPenParam.CornerDelay)
                {
                    BC2_SetScannerDelay_List(CardID, currentPenParam.JumpEndDelay, currentPenParam.MarkEndDelay, currentPenParam.CornerDelay);
                }

                if (lastPenParam.MinJumpDelay != currentPenParam.MinJumpDelay || lastPenParam.JumpLengthLimit != currentPenParam.JumpLengthLimit)
                {
                    BC2_SetVarJumpDelayParam_List(CardID, currentPenParam.MinJumpDelay, currentPenParam.JumpLengthLimit);
                }

                if (lastPenParam.CornerDelayMode != currentPenParam.CornerDelayMode || lastPenParam.CornerDelayEdge != currentPenParam.CornerDelayEdge)
                {
                    BC2_SetCornerDelayMode_List(CardID, currentPenParam.CornerDelayMode, currentPenParam.CornerDelayEdge);
                }

                if (lastPenParam.Power != currentPenParam.Power)
                {
                    if (PowerMode == 1)
                    {
                        BC2_SetLaserDigital_List(CardID, (int)((penParam.Power / 100) * 255));
                    }
                    else if (PowerMode == 2)
                    {
                        //Console.WriteLine(penParam.Power);
                        //Console.WriteLine((penParam.Power / 100) * 4 * 1000);
                        //Console.WriteLine((int)(penParam.Power / 100) * 4 * 1000);
                        BC2_SetLaserDA_List(CardID, 1, (int)((penParam.Power / 100) * 4 * 1000));
                        if (penParam.SecondLaserParamEnable == 1)
                        {
                            BC2_SetLaserDA_List(CardID, 2, (int)((penParam.OuterPower / 100) * 4 * 1000));
                        }
                    }
                    else if (PowerMode == 3)
                    {
                        BC2_SetLaserDA_List(CardID, 1, (int)((penParam.Power / 100) * 5 * 1000));
                        if (penParam.SecondLaserParamEnable == 1)
                        {
                            BC2_SetLaserDA_List(CardID, 2, (int)((penParam.OuterPower / 100) * 5 * 1000));
                        }
                    }
                    else if (PowerMode == 4)
                    {
                        BC2_SetLaserDA_List(CardID, 1, (int)((penParam.Power / 100) * 10 * 1000));
                        if (penParam.SecondLaserParamEnable == 1)
                        {
                            BC2_SetLaserDA_List(CardID, 2, (int)((penParam.OuterPower / 100) * 10 * 1000));
                        }
                    }
                }

                if (LaserMode == 1)
                {
                    if (lastPenParam.PrrFrequency != currentPenParam.PrrFrequency || lastPenParam.PrrDutyRatio != currentPenParam.PrrDutyRatio)
                    {
                        BC2_SetLaserPRR_List(CardID, penParam.PrrFrequency * 1000, penParam.PrrDutyRatio / 100);
                    }
                }
                else
                {
                    if (lastPenParam.PrrFrequency != currentPenParam.PrrFrequency || lastPenParam.PrrDutyRatio != currentPenParam.PrrDutyRatio)
                    {
                        BC2_SetLaserAP_List(CardID, penParam.PwmFrequency * 1000, penParam.PwmDutyRatio / 100);
                    }

                }
            }

            ///
            ///添加第一个Jump去起点,并加工
            ///
            TGalvoPt2d JumpToShapeHead(int CardID, TShapeParam shapeParam, TPenParam currentPenParam)
            {
                TGalvoPt2d PS = new TGalvoPt2d();
                // 是图元
                if (shapeParam.ShapeType != 1)
                {
                    // 非1
                    if (shapeParam.ShapeType == 2)
                    {
                        for (int i = 0; i < currentPenParam.ProcessCount; i++)   //根据加工次数重复添加图元，这里也可以使用RepeatList+UntilList
                        {
                            BC2_JumpLineAbs_List(CardID, shapeParam.Pos.X, shapeParam.Pos.Y);
                            BC2_MarkLineAbs_List(CardID, shapeParam.Pos.X, shapeParam.Pos.Y);
                        }
                        PS.X = shapeParam.Pos.X;
                        PS.Y = shapeParam.Pos.Y;
                        firstJump = 1;
                        return PS;
                    }
                    else if (shapeParam.ShapeType == 3)
                    {
                        for (int i = 0; i < currentPenParam.ProcessCount; i++)
                        {
                            BC2_JumpLineAbs_List(CardID, shapeParam.PtStart.X, shapeParam.PtStart.Y);
                            BC2_MarkLineAbs_List(CardID, shapeParam.PtEnd.X, shapeParam.PtEnd.Y);
                        }
                        PS.X = shapeParam.PtEnd.X;
                        PS.Y = shapeParam.PtEnd.Y;
                        firstJump = 1;
                        return PS;
                    }
                    else if (shapeParam.ShapeType == 4)
                    {
                        double[] pointss = new double[shapeParam.PtCount * 2];
                        Marshal.Copy(shapeParam.PointArr, pointss, 0, shapeParam.PtCount * 2);
                        for (int i = 0; i < currentPenParam.ProcessCount; i++)
                        {
                            BC2_JumpLineAbs_List(CardID, pointss[0], pointss[1]);
                            for (int j = 2; j < shapeParam.PtCount * 2; j += 2)
                            {
                                BC2_MarkLineAbs_List(CardID, pointss[j], pointss[j + 1]);
                            }
                        }
                        PS.X = pointss[shapeParam.PtCount * 2 - 2];
                        PS.Y = pointss[shapeParam.PtCount * 2 - 1];
                        firstJump = 1;
                        return PS;
                    }
                    else if (shapeParam.ShapeType == 5)
                    {
                        for (int i = 0; i < currentPenParam.ProcessCount; i++)
                        {

                            BC2_JumpLineAbs_List(CardID, shapeParam.ArcStart.X, shapeParam.ArcStart.Y);
                            BC2_MarkArcAbs_List(CardID, shapeParam.ArcCenter.X, shapeParam.ArcCenter.Y, shapeParam.ArcAngle);
                        }
                        // 计算圆弧的半径
                        double r = Math.Sqrt(Math.Pow(shapeParam.ArcStart.X - shapeParam.ArcCenter.X, 2) +
                                             Math.Pow(shapeParam.ArcStart.Y - shapeParam.ArcCenter.Y, 2));
                        // 计算起点到圆心的角度
                        double angle1 = Math.Atan2(shapeParam.ArcStart.Y - shapeParam.ArcCenter.Y,
                                                   shapeParam.ArcStart.X - shapeParam.ArcCenter.X);
                        // 将角度转化为弧度
                        double angleInRadians = shapeParam.ArcAngle * (Math.PI / 180);
                        // 计算目标角度（旋转后的角度）
                        double angle2 = angle1 + angleInRadians;
                        // 计算圆弧结尾的坐标
                        PS.X = shapeParam.ArcCenter.X + r * Math.Cos(angle2);
                        PS.Y = shapeParam.ArcCenter.Y + r * Math.Sin(angle2);
                        firstJump = 1;
                        return PS;
                    }
                }
                else
                {
                    BC2_SetWobbleMode_List(CardID, shapeParam.WobbleMode, shapeParam.WobbleDirection);
                    if (shapeParam.WobbleMode != 0)
                    {
                        BC2_SetWobbleParam_List(CardID, shapeParam.WobbleAmp, shapeParam.WobbleAmpExt, shapeParam.WobblePeriod, shapeParam.WobblePhase0);
                        BC2_SetWobbleOffset_List(CardID, shapeParam.WobbleAmpOffset, shapeParam.WobbleAmpExtOffset);
                    }
                    BC2_SetSkyWritingTime_List(CardID, shapeParam.DelayIn, shapeParam.DelayOut);
                    BC2_SetSkyWritingLaserDelay_List(CardID, (long)shapeParam.LaserOffDelay, (long)(shapeParam.LaserOnDelay - shapeParam.LaserOffDelay));
                    BC2_SetSkyWritingCornerLimit_List(CardID, shapeParam.Angle);
                    return PS;
                }
                return PS;
            }
            ///
            //发送后续加工指令
            ///
            TGalvoPt2d SendMarkCommand(int CardID, TShapeParam shapeParam, TGalvoPt2d lastposition, TPenParam currentPenParam)
            {
                TGalvoPt2d PS = new TGalvoPt2d();
                // 是图元
                if (shapeParam.ShapeType != 1)
                {
                    // 是点
                    if (shapeParam.ShapeType == 2)
                    {
                        for (int i = 0; i < currentPenParam.ProcessCount; i++)
                        {
                            BC2_JumpLineAbs_List(CardID, shapeParam.Pos.X, shapeParam.Pos.Y);
                            BC2_MarkLineAbs_List(CardID, shapeParam.Pos.X, shapeParam.Pos.Y);
                        }
                        PS.X = shapeParam.Pos.X;
                        PS.Y = shapeParam.Pos.Y;
                        return PS;
                    }
                    else if (shapeParam.ShapeType == 3)
                    {
                        if (Math.Sqrt(Math.Pow(lastposition.X - shapeParam.PtStart.X, 2) + Math.Pow(lastposition.Y - shapeParam.PtStart.Y, 2)) > 1e-6)
                        {
                            for (int i = 0; i < currentPenParam.ProcessCount; i++)
                            {
                                BC2_JumpLineAbs_List(CardID, shapeParam.PtStart.X, shapeParam.PtStart.Y);
                                BC2_MarkLineAbs_List(CardID, shapeParam.PtEnd.X, shapeParam.PtEnd.Y);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < currentPenParam.ProcessCount; i++)
                            {
                                BC2_MarkLineAbs_List(CardID, shapeParam.PtStart.X, shapeParam.PtStart.Y);
                                BC2_MarkLineAbs_List(CardID, shapeParam.PtEnd.X, shapeParam.PtEnd.Y);
                            }
                        }
                        PS.X = shapeParam.PtEnd.X;
                        PS.Y = shapeParam.PtEnd.Y;
                        return PS;

                    }
                    else if (shapeParam.ShapeType == 4)
                    {
                        double[] pointss = new double[shapeParam.PtCount * 2];
                        Marshal.Copy(shapeParam.PointArr, pointss, 0, shapeParam.PtCount * 2);
                        if (Math.Sqrt(Math.Pow(lastposition.X - pointss[0], 2) + Math.Pow(lastposition.Y - pointss[1], 2)) > 1e-6)
                        {
                            for (int i = 0; i < currentPenParam.ProcessCount; i++)
                            {
                                BC2_JumpLineAbs_List(CardID, pointss[0], pointss[1]);
                                for (int j = 2; j < shapeParam.PtCount * 2; j += 2)
                                {
                                    BC2_MarkLineAbs_List(CardID, pointss[j], pointss[j + 1]);
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < currentPenParam.ProcessCount; i++)
                            {
                                for (int j = 0; j < shapeParam.PtCount * 2; j += 2)
                                {
                                    BC2_MarkLineAbs_List(CardID, pointss[j], pointss[j + 1]);
                                }
                            }

                        }
                        PS.X = pointss[shapeParam.PtCount * 2 - 2];
                        PS.Y = pointss[shapeParam.PtCount * 2 - 1];
                        firstJump = 1;
                        return PS;
                    }
                    else if (shapeParam.ShapeType == 5)
                    {
                        if (Math.Sqrt(Math.Pow(lastposition.X - shapeParam.ArcStart.X, 2) + Math.Pow(shapeParam.ArcStart.Y, 2)) > 1e-6)
                        {
                            for (int i = 0; i < currentPenParam.ProcessCount; i++)
                            {
                                BC2_JumpLineAbs_List(CardID, shapeParam.ArcStart.X, shapeParam.ArcStart.Y);
                                BC2_MarkArcAbs_List(CardID, shapeParam.ArcCenter.X, shapeParam.ArcCenter.Y, shapeParam.ArcAngle);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < currentPenParam.ProcessCount; i++)
                            {
                                BC2_MarkLineAbs_List(CardID, shapeParam.ArcStart.X, shapeParam.ArcStart.Y);
                                BC2_MarkArcAbs_List(CardID, shapeParam.ArcCenter.X, shapeParam.ArcCenter.Y, shapeParam.ArcAngle);
                            }
                        }

                        // 计算圆弧的半径
                        double r = Math.Sqrt(Math.Pow(shapeParam.ArcStart.X - shapeParam.ArcCenter.X, 2) +
                                             Math.Pow(shapeParam.ArcStart.Y - shapeParam.ArcCenter.Y, 2));
                        // 计算起点到圆心的角度
                        double angle1 = Math.Atan2(shapeParam.ArcStart.Y - shapeParam.ArcCenter.Y,
                                                   shapeParam.ArcStart.X - shapeParam.ArcCenter.X);
                        // 将角度转化为弧度
                        double angleInRadians = shapeParam.ArcAngle * (Math.PI / 180);
                        // 计算目标角度（旋转后的角度）
                        double angle2 = angle1 + angleInRadians;
                        // 计算圆弧结尾的坐标
                        PS.X = shapeParam.ArcCenter.X + r * Math.Cos(angle2);
                        PS.Y = shapeParam.ArcCenter.Y + r * Math.Sin(angle2);
                        firstJump = 1;
                        return PS;
                    }
                }
                else
                {
                    BC2_SetWobbleMode_List(CardID, shapeParam.WobbleMode, shapeParam.WobbleDirection);
                    if (shapeParam.WobbleMode != 0)
                    {
                        BC2_SetWobbleParam_List(CardID, shapeParam.WobbleAmp, shapeParam.WobbleAmpExt, shapeParam.WobblePeriod, shapeParam.WobblePhase0);
                        BC2_SetWobbleOffset_List(CardID, shapeParam.WobbleAmpOffset, shapeParam.WobbleAmpExtOffset);
                    }
                    BC2_SetSkyWritingTime_List(CardID, shapeParam.DelayIn, shapeParam.DelayOut);
                    BC2_SetSkyWritingLaserDelay_List(CardID, (long)shapeParam.LaserOffDelay, (long)(shapeParam.LaserOnDelay - shapeParam.LaserOffDelay));
                    BC2_SetSkyWritingCornerLimit_List(CardID, shapeParam.Angle);
                    return lastposition;
                }
                return PS;
            }

            
            //进行图纸解析
            for (int i = 0; i < shapeCount; i++)
            {
                //获取图元信息、图元对应的图层信息
                ErrCode=BC2_GetFileShapeInfo(file, i + 1, ref shapeParam);
                if(ErrCode != 0) { return 1; }
                ErrCode = BC2_GetFilePenParams(file, shapeParam.PenNum, ref penParam);
                if(ErrCode != 0) { return 1; }
                //如果是第一个图层，写入所有的笔号参数
                if (i == 0)
                {
                    BC2_SetMarkSpeed_List(CardID, penParam.MarkSpeed);
                    BC2_SetJumpSpeed_List(CardID, penParam.JumpSpeed);
                    BC2_SetLaserDelay_List(CardID, penParam.LaserOnDelay, penParam.LaserOffDelay);
                    BC2_SetScannerDelay_List(CardID, penParam.JumpEndDelay, penParam.MarkEndDelay, penParam.CornerDelay);
                    BC2_SetVarJumpDelayParam_List(CardID, penParam.MinJumpDelay, penParam.JumpLengthLimit);
                    BC2_SetCornerDelayMode_List(CardID, penParam.CornerDelayMode, penParam.CornerDelayEdge);
                    //功率值写入
                    if (PowerMode == 1)
                    {
                        BC2_SetLaserDigital_List(CardID, (int)((penParam.Power / 100) * 255));
                    }
                    else if (PowerMode == 2)
                    {
                        ErrCode = BC2_SetLaserDA_List(CardID, 1, (int)((penParam.Power / 100) * 4 * 1000));
                        if (penParam.SecondLaserParamEnable == 1)
                        {
                            BC2_SetLaserDA_List(CardID, 2, (int)((penParam.OuterPower / 100) * 4 * 1000));
                        }
                    }
                    else if (PowerMode == 3)
                    {
                        BC2_SetLaserDA_List(CardID, 1, (int)((penParam.Power / 100) * 5 * 1000));
                        if (penParam.SecondLaserParamEnable == 1)
                        {
                            BC2_SetLaserDA_List(CardID, 2, (int)((penParam.OuterPower / 100) * 5 * 1000));
                        }
                    }
                    else if (PowerMode == 4)
                    {
                        BC2_SetLaserDA_List(CardID, 1, (int)((penParam.Power / 100) * 10 * 1000));
                        if (penParam.SecondLaserParamEnable == 1)
                        {
                            BC2_SetLaserDA_List(CardID, 2, (int)((penParam.OuterPower / 100) * 10 * 1000));
                        }
                    }
                    if (LaserMode == 1)
                    {
                        BC2_SetLaserAP_List(CardID, 100, 1);
                        BC2_SetLaserPRR_List(CardID, penParam.PrrFrequency * 1000, penParam.PrrDutyRatio / 100);
                    }
                    else
                    {
                        BC2_SetLaserAP_List(CardID, penParam.PwmFrequency * 1000, penParam.PwmDutyRatio / 100);
                    }

                    lastPenParam = penParam;

                    //写入加工指令
                    //跳转到图元起始位置、正常发Mark指令/没跳就是设置指令
                    LastPosition = JumpToShapeHead(CardID, shapeParam, penParam);

                }
                else if (lastPenParam.PenNum == shapeParam.PenNum)
                {//不是第一个图层，图层编号没有变化, 直接执行写图元指令

                    if (firstJump == 0)
                    {
                        //跳转到图元起始位置
                        LastPosition = JumpToShapeHead(CardID, shapeParam, penParam);
                    }
                    else
                    {
                        //非加工指令,直接发加工
                        //是加工指令判断是否连续，如果连续就Jump到起点，如果不连续就Mark指令
                        SendMarkCommand(CardID, shapeParam, LastPosition, penParam);
                    }
                }
                else if (lastPenParam.PenNum != shapeParam.PenNum)
                {
                    UpdateChangedPenParams(lastPenParam, penParam);
                    if (firstJump == 0)
                    {
                        //跳转到图元起始位置
                        LastPosition = JumpToShapeHead(CardID, shapeParam, penParam);
                    }
                    else
                    {
                        //非加工指令,直接发加工
                        //是加工指令判断是否连续，如果连续就Jump到起点，如果不连续就Mark指令
                        SendMarkCommand(CardID, shapeParam, LastPosition, penParam);
                    }
                    lastPenParam = penParam;
                }
            }
            BC2_SetEndOfList_List(CardID);
            return 0;
        }

        public static int StartListAndWaitOver(int CardID, int ListPos, int ListNum = 1)
        {
            int res;
            res = BC2_StartExecuteList(CardID, ListPos);
            WaitOver(CardID);
            return res;
        }
    }

}
