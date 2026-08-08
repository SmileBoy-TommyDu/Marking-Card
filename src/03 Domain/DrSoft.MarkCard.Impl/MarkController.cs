using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.DTO;
using DrSoft.MarkCard.Model.MarkCommand;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Text;



namespace DrSoft.MarkCard.Impl
{
    public class MarkController : IMarkController
    {

        private IMarkCardAdapter _markCard;
        private ILogger<MarkController> _logger;
        private readonly Dictionary<ShapeType, IShapeCommandGenerator> _generators;

        public event Action<uint, MarkingState> OnMarkingEnd;

        public MarkController(IMarkCardAdapter markCard, ILogger<MarkController> logger)
        {
            _markCard = markCard;
            _logger = logger;
            //_generators = generators.ToDictionary(g => g.SupportedType);
            // 通过反射自动加载所有实现了 IShapeCommandGenerator 的类，并根据 SupportedType 属性构建字典
            var generatorInterfaceType = typeof(IShapeCommandGenerator);
            var generatorInstances = typeof(MarkController).Assembly
                .GetTypes()
                .Where(t => generatorInterfaceType.IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !t.IsInterface
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => Activator.CreateInstance(t) as IShapeCommandGenerator)
                .Where(g => g != null)
                .Cast<IShapeCommandGenerator>()
                .ToList();

            _generators = generatorInstances
                .GroupBy(g => g.SupportedType)
                .ToDictionary(g => g.Key, g => g.First());
        }

        public MarkErrorCode Initialize()
        {

            _markCard.OnMarkingEnd += OnMarkingEnd;
            return _markCard.Initialize();


        }



        #region LoadMarkData

        public MarkErrorCode LoadFile(uint cardNo, string fileName)
        {

            return MarkErrorCode.None;
        }



        public MarkErrorCode LoadMarkData(uint cardNo, MarkingJobDto markData)
        {
            if (markData?.Shapes == null  || markData.AdvancedFeatureParamMap == null || markData.ParameterMap == null || markData.Shapes.Count <= 0 || markData.ParameterMap.Count <= 0)
            {
                _logger.LogInformation("Invalid mark data. Marking job data or its properties are null or empty.");
                return MarkErrorCode.NoGraphicData;
            }

            var commands = new List<IMarkCommand>();

            // 全局加工次数，默认为1（不重复）
            int processTimes = Math.Max(1, markData.ProcessTimes);

            for (int t = 0; t < processTimes; t++)
            {
                // 每轮加工重置状态，确保参数命令在每轮开头正确生成
                ProcessParam? currentProcessParam = null;
                SkyWritingCommand lastSkyWritingCommand = null;

                foreach (var draw in markData.Shapes)
                {
                    if (draw == null)
                    {
                        _logger.LogInformation($"Invalid mark data. Draw object is null.");
                        return MarkErrorCode.GraphicPrimitiveDataError;
                    }

                

                    if (!markData.ParameterMap.TryGetValue(draw.UId, out ProcessParam? processParam) || processParam == null)
                    {
                        _logger.LogError($"Invalid mark data. No process parameter found for draw object with id {draw.UId}.");
                        return MarkErrorCode.UnFoundGraphicPrimitiveProcessParam;
                    }

                    // 检查是否有高级特征参数（如飞行写入）需要应用
                    markData.AdvancedFeatureParamMap.TryGetValue(draw.UId, out AdvancedFeatureParam? advancedFeatureParam);
                    if (advancedFeatureParam != null) { 
                        if(advancedFeatureParam.SkyWritingModel > 0)
                        {
                            var swCommand = new SkyWritingCommand
                            {
                                SkyWritingModel = advancedFeatureParam.SkyWritingModel,
                                //Enabled = true,
                                Timelag = (float)advancedFeatureParam.DelayTime,
                                LaserOnShift = (float)advancedFeatureParam.LaserOnDelay,
                                Nprev = (float)advancedFeatureParam.RunInTime,
                                Npost = (float)advancedFeatureParam.RunOutTime,
                                AngleLimit = (float)advancedFeatureParam.ExtremeAngle
                            };

                            if(swCommand.Equals(lastSkyWritingCommand) == false)
                            {
                                
                                commands.Add(new SkyWritingCommand()
                                {
                                    SkyWritingModel = swCommand.SkyWritingModel,
                                    //Enabled = true,
                                    Timelag =swCommand.Timelag,
                                    LaserOnShift = swCommand.LaserOnShift,
                                    Nprev = swCommand.Nprev,
                                    Npost = swCommand.Npost,
                                    AngleLimit = swCommand.AngleLimit
                                });
                                lastSkyWritingCommand = swCommand;
                            }

                        }
                        else
                        {
                            if(lastSkyWritingCommand != null && lastSkyWritingCommand.SkyWritingModel > 0)
                            {
                                lastSkyWritingCommand.SkyWritingModel = 0;
                                commands.Add(new SkyWritingCommand()
                                {
                                    SkyWritingModel = lastSkyWritingCommand.SkyWritingModel,
                                    //Enabled = true,
                                    Timelag = lastSkyWritingCommand.Timelag,
                                    LaserOnShift = lastSkyWritingCommand.LaserOnShift,
                                    Nprev = lastSkyWritingCommand.Nprev,
                                    Npost = lastSkyWritingCommand.Npost,
                                    AngleLimit = lastSkyWritingCommand.AngleLimit
                                });
                                lastSkyWritingCommand = null;
                            }
                        }
                    }
                    else
                    {
                        if(lastSkyWritingCommand != null&&lastSkyWritingCommand.SkyWritingModel>0)
                        {
                            lastSkyWritingCommand.SkyWritingModel = 0;
                            commands.Add(new SkyWritingCommand()
                            {
                                SkyWritingModel = lastSkyWritingCommand.SkyWritingModel,
                                //Enabled = true,
                                Timelag = lastSkyWritingCommand.Timelag,
                                LaserOnShift = lastSkyWritingCommand.LaserOnShift,
                                Nprev = lastSkyWritingCommand.Nprev,
                                Npost = lastSkyWritingCommand.Npost,
                                AngleLimit = lastSkyWritingCommand.AngleLimit
                            });
                            lastSkyWritingCommand = null;
                        }
                    }


                    if (_generators.TryGetValue(draw.Type, out var generator))
                    {
                        if(generator.Validate(draw) == false)
                        {
                            _logger.LogInformation($"Invalid mark data. Draw object with id {draw.UId} failed validation for type {draw.Type}.");
                            return MarkErrorCode.GraphicPrimitiveDataError;
                        }
                        commands.AddRange(generator.Generate(draw, processParam, advancedFeatureParam, ref currentProcessParam));
                    }
                    else
                    {
                        _logger.LogWarning($"Unsupported draw object type: {draw.Type}, id={draw.UId}");
                    }
                }

                //每轮加工结束后，关闭SkyWriting模式
                if (lastSkyWritingCommand != null&& lastSkyWritingCommand.SkyWritingModel!=0)
                {
                    var skyCommand = new SkyWritingCommand() { SkyWritingModel = 0 };
                    commands.Add(skyCommand);
                }
            }

            // 调试：将命令序列导出为 CSV
            SaveCommandsToCsv(commands);

            return _markCard.LoadMarkData(cardNo, commands);
        }

        /// <summary>
        /// 将 MarkCommand 列表导出为 CSV 文件，供调试分析
        /// </summary>
        private void SaveCommandsToCsv(List<IMarkCommand> commands)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Index,CommandType,X,Y,EndPointX,EndPointY,CenterX,CenterY,Radius,Angle,MajorRadius,MinorRadius,Alpha,Power,Frequency,PulsesWidth,JumpSpeed,MarkSpeed,LaserOnDelay,LaserOffDelay,MarkDelay,JumpDelay,CornerDelay,DotDuration,Enabled,Timelag,LaserOnShift,Nprev,Npost,AngleLimit,DashArray");

                for (int i = 0; i < commands.Count; i++)
                {
                    var cmd = commands[i];
                    string line = cmd switch
                    {
                        JumpCommand c => $"{i},{c.MarkCommandType},{c.Point.X:F4},{c.Point.Y:F4},,,,,,,,,,,,,,,,,,,,,,,,,,,,,,",
                        MarkPointCommand c => $"{i},{c.MarkCommandType},{c.Point.X:F4},{c.Point.Y:F4},,,,,,,,,,,,,,,,,,,{c.DotDuration:F2},,,,,,,,,",
                        MarkLineCommand c => $"{i},{c.MarkCommandType},{c.EndPoint.X:F4},{c.EndPoint.Y:F4},,,,,,,,,,,,,,,,,,,,,,,,,,,,,,",
                        MarkCircleCommand c => $"{i},{c.MarkCommandType},,,,,{c.Center.X:F4},{c.Center.Y:F4},{c.Radius:F4},{c.Angle:F4},,,,,,,,,,,,,,,,,,,,,",
                        MarkEllipseCommand c => $"{i},{c.MarkCommandType},,,,,{c.Center.X:F4},{c.Center.Y:F4},{c.StartAngle:F4},{c.SweepAngle:F4},{c.MajorRadius:F4},{c.MinorRadius:F4},{c.Alpha:F4},,,,,,,,,,,,,,,,,,",
                        MarkDashedLineCommand c => $"{i},{c.MarkCommandType},{c.StartPoint?.X.ToString("F4") ?? ""},{c.StartPoint?.Y.ToString("F4") ?? ""},{c.EndPoint?.X.ToString("F4") ?? ""},{c.EndPoint?.Y.ToString("F4") ?? ""},,,,,,,,,,,,,,,,,,,,,,,{FormatDashArray(c.DashArray)},",
                        ModifyPowerCommand c => $"{i},{c.MarkCommandType},,,,,,,,,,,,,,{c.Power:F2},,,,,,,,,,,,,,,,,,,,,",
                        ModifyFrequencyAndPulsesWidthCommand c => $"{i},{c.MarkCommandType},,,,,,,,,,,,,,,{c.Frequency:F2},{c.PulsesWidth:F2},,,,,,,,,,,,,,,,,,,",
                        ModifySpeedCommand c => $"{i},{c.MarkCommandType},,,,,,,,,,,,,,,,{c.JumpSpeed:F2},{c.MarkSpeed:F2},,,,,,,,,,,,,,,,,",
                        ModifyLaserDelayCommand c => $"{i},{c.MarkCommandType},,,,,,,,,,,,,,,,,,,,{c.LaserOnDelay},{c.LaserOffDelay},,,,,,,,,,,,,,,,,",
                        ModifyScannerDelayCommand c => $"{i},{c.MarkCommandType},,,,,,,,,,,,,,,,,,,,,,{c.MarkDelay},{c.JumpDelay},{c.CornerDelay},,,,,,,,,,,,,,,",
                        SkyWritingCommand c => $"{i},{c.MarkCommandType},,,,,,,,,,,,,,,,,,,,,,,,,,,{(c.SkyWritingModel > 0 ? 1 : 0)},{c.Timelag:F4},{c.LaserOnShift:F4},{c.Nprev:F4},{c.Npost:F4},{c.AngleLimit:F4}",
                        MarkBitmapCommand => $"{i},{cmd.MarkCommandType},,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,",
                        _ => $"{i},{cmd.MarkCommandType},,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,"
                    };
                    sb.AppendLine(line);
                }

                var dir = Path.Combine(AppContext.BaseDirectory, "DebugLogs");
                Directory.CreateDirectory(dir);
                var filePath = Path.Combine(dir, $"MarkCommands_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                _logger.LogInformation($"MarkCommand CSV exported: {filePath} ({commands.Count} commands)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to export MarkCommand CSV");
            }
        }

        private static string FormatDashArray(List<PointF>? dashArray)
        {
            if (dashArray == null || dashArray.Count == 0) return "";
            return "\"" + string.Join(";", dashArray.Select(p => $"({p.X:F2},{p.Y:F2})")) + "\"";
        }

        #endregion

        public MarkErrorCode Pause()
        {
            return _markCard.Pause();
        }

        public MarkErrorCode StartMarking(uint cardNo)
        {
            return _markCard.StartMarking(cardNo);

        }

        public MarkErrorCode Pause(uint cardNo)
        {
            return _markCard.Pause(cardNo);
        }

        public MarkErrorCode Resume()
        {
            return _markCard.Resume();
        }

        public MarkErrorCode Resume(uint cardNo)
        {
            return _markCard.Resume(cardNo);
        }

        public MarkErrorCode LaserOn()
        {
            return _markCard.LaserOn();
        }

        public MarkErrorCode LaserOn(uint cardNo)
        {
            return _markCard.LaserOn(cardNo);
        }

        public MarkErrorCode LaserOff()
        {
            return _markCard.LaserOff();
        }

        public MarkErrorCode LaserOff(uint cardNo)
        {
            return _markCard.LaserOff(cardNo);
        }

        public MarkErrorCode SetMarkingMode(MarkingMode mode)
        {
            return _markCard.SetMarkingMode(mode);
        }

        public MarkErrorCode SetMarkingMode(uint cardNo, MarkingMode mode)
        {
            return _markCard.SetMarkingMode(cardNo, mode);
        }

        public MarkErrorCode SetTransformMatrix(uint cardNo, uint headID, float m00, float m01, float m10, float m11)
        {
            return _markCard.SetTransformMatrix(cardNo, headID, m00, m01, m10, m11);
        }

        public MarkErrorCode SetScale(uint cardNo, uint headID, double transformScale)
        {
            return _markCard.SetScale(cardNo, headID, transformScale);
        }

        public MarkErrorCode SetOffset(uint cardNo, uint headID, double xOffset, double yOffset, double angleOffset)
        {
           return _markCard.SetOffset(cardNo, headID, xOffset, yOffset, angleOffset);
        }

        public MarkErrorCode GetLaserFrequency(uint cardNo, out double frequency)
        {
            return _markCard.GetLaserFrequency(cardNo, out frequency);
        }

        public MarkErrorCode GetLaserPulseWidth(uint cardNo, out double pulseWidth)
        {
            return _markCard.GetLaserPulseWidth(cardNo, out pulseWidth);
        }

        public MarkErrorCode SetScannerSpeed(uint cardNo, double jumpSpeed, double markSpeed)
        {
            return _markCard.SetScannerSpeed(cardNo, jumpSpeed, markSpeed);
        }

        public MarkErrorCode GetJumpDelay(uint cardNo, out double jumpDelay)
        {
            return _markCard.GetJumpDelay(cardNo, out jumpDelay );
        }

        public MarkErrorCode GetMarkingDelay(uint cardNo, out double markingDelay)
        {
            return _markCard.GetMarkingDelay(cardNo, out markingDelay);
        }

        public MarkErrorCode GetPolygonDelay(uint cardNo, out double polygonDelay)
        {
            return _markCard.GetPolygonDelay(cardNo, out polygonDelay);
        }

        public MarkErrorCode GetLaserDelay(uint cardNo, out double laserOnDelay, out double laserOffDelay)
        {
            return _markCard.GetLaserDelay(cardNo, out laserOnDelay, out laserOffDelay);
        }

        public MarkErrorCode GetScannerConnect(uint cardNo, uint headID, out bool connectFlag)
        {
            return _markCard.GetScannerConnect(cardNo, headID, out connectFlag);
        }

        public MarkErrorCode GetScannerPosion(uint cardNo, uint headID, out PointF point)
        {
            return _markCard.GetScannerPosion(cardNo, headID, out point);
        }

        public MarkErrorCode GetScannerTemperature(uint cardNo, uint headID, out double temperatureX, out double temperatureY)
        {
            return _markCard.GetScannerTemperature(cardNo, headID, out temperatureX, out temperatureY);
        }

        public int GetEstimatedExecTime(uint cardNo)
        {
            return _markCard.GetEstimatedExecTime(cardNo);
        }

        public MarkErrorCode GetRealExecTime(uint cardNo,out int execTime)
        {
            return _markCard.GetRealExecTime(cardNo,out execTime);
        }

        public MarkErrorCode ReadDigitalInput(uint cardNo, out bool[] value)
        {
            return _markCard.ReadDigitalInput(cardNo, out value);
        }

        public MarkErrorCode ReadDigitalOutput(uint cardNo, out bool[] value)
        {
            return _markCard.ReadDigitalOutput(cardNo, out value);
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, uint signalIndex, bool setParam)
        {
            return _markCard.WriteDigitalOutput(cardNo, signalIndex, setParam);
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, bool[] setParam)
        {
            return _markCard.WriteDigitalOutput(cardNo, setParam);
        }

        public MarkErrorCode GetScannerAcc(uint cardNo, uint headID, out double acc)
        {
            return _markCard.GetScannerAcc(cardNo, headID, out acc);
        }

        public MarkErrorCode LoadCalibrationFile(string? head1File, string? head2File)
        {
            return _markCard.LoadCalibrationFile(head1File, head2File);
        }

        public MarkErrorCode GetMarkingMode(uint cardNo, out MarkingMode mode)
        {
            return _markCard.GetMarkingMode(cardNo, out mode);
        }

        public MarkErrorCode GetMarkingState(uint cardNo, out MarkingState state)
        {
            return _markCard.GetMarkingState(cardNo, out state);
        }

        public MarkErrorCode LoadCalibrationFile(uint cardNo, string? head1File, string? head2File)
        {
            return _markCard.LoadCalibrationFile(cardNo, head1File, head2File);
        }

        public MarkErrorCode CreateCalibrationFile(string srcFile, string dstFile, double[] targetPostX, double[] targetPostY, double[] realsPostX, double[] realsPostY)
        {
            return _markCard.CreateCalibrationFile(srcFile, dstFile, targetPostX, targetPostY, realsPostX, realsPostY);
        }

        public MarkErrorCode SetScannerDelay(uint cardNo, int markDelay, int jumpDelay, int polygonDelay)
        {
            return _markCard.SetScannerDelay(cardNo, markDelay, jumpDelay, polygonDelay);
        }

        public MarkErrorCode SetLaserDelay(uint cardNo, int laserOnDelay, int laserOffDelay)
        {
            return _markCard.SetLaserDelay(cardNo, laserOnDelay, laserOffDelay);
        }

        public MarkErrorCode SetLaserFrequencyAndPulseWidth(uint cardNo, double frequency, double pulseWidth)
        {
            return _markCard.SetLaserFrequencyAndPulseWidth(cardNo, frequency, pulseWidth);
        }

        public MarkErrorCode SetLaserFrequency(uint cardNo, double frequency)
        {
            return _markCard.SetLaserFrequency(cardNo, frequency);
        }

        public MarkErrorCode StopMarking(uint cardNo)
        {
            return _markCard.StopMarking(cardNo);
        }

        public MarkErrorCode StartMarking()
        {
            return _markCard.StartMarking();
        }

        public MarkErrorCode StopMarking()
        {
            return _markCard.StopMarking();
        }

        public MarkErrorCode SetLaserPower(uint cardNo, double power)
        {
            return _markCard.SetLaserPower(cardNo, power);
        }

        public MarkErrorCode SetBarrelCorrection(uint cardNo, double idealWidth, double idealHeight, double[] widthParam, double[] heightParam)
        {
            return _markCard.SetBarrelCorrection(cardNo, idealWidth, idealHeight, widthParam, heightParam);
        }

        public void Dispose()
        {
            _markCard.Dispose();
        }
    }
}
