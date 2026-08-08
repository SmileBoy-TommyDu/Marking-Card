using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DrSoft.MarkCard.Service
{
    /// <summary>
    /// 校正服务实现类
    /// </summary>
    public class CalibrationService
    {
        private readonly IMarkController _markController;
        private readonly ILogger<CalibrationService> _logger;
        private readonly string _configFile;
        private ProcessParam _calibrationProcessParam = null;

        public CalibrationService(IMarkController markController, ILogger<CalibrationService> logger)
        {
            _markController = markController;
            _logger = logger;
            _configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibrationProcessParam.json");
        }

        /// <inheritdoc />
        public MarkErrorCode LoadCalibrationFile(uint cardNo, string? head1File, string? head2File)
        {
            _logger.LogInformation(
                "正在加载校正文件到打标卡 {CardNo}: Head1={Head1File}, Head2={Head2File}",
                cardNo, head1File, head2File);

            var result = _markController.LoadCalibrationFile(cardNo, head1File, head2File);

            if (result == MarkErrorCode.None)
            {
                _logger.LogInformation("校正文件加载成功");
            }
            else
            {
                _logger.LogError("校正文件加载失败: {ErrorCode}", result);
            }

            return result;
        }


        public ProcessParam GetCalibrationProcessParam()
        {
            //从程序运行目录获取校正参数配置文件（calibrationProcessParam.json）,如果文件不存在，则返回默认参数
            if (_calibrationProcessParam == null)
            {
                if (File.Exists(_configFile))
                {

                    using (var reader = new StreamReader(_configFile))
                    {
                        var json = reader.ReadToEnd();
                        _calibrationProcessParam = JsonSerializer.Deserialize<ProcessParam>(json) ?? new ProcessParam();
                    }
                }
                else
                {
                    _calibrationProcessParam = new ProcessParam();

                }
            }
            
            return _calibrationProcessParam;
        }


        public void SaveCalibrationProcessParam(ProcessParam param)
        {
            _calibrationProcessParam = param;
            //将校正参数保存到程序运行目录的calibrationProcessParam.json文件中
            var json = JsonSerializer.Serialize(param);
            File.WriteAllText(_configFile, json);
            _logger.LogInformation("校正参数已保存到 {ConfigFile}", _configFile);
        }

        /// <inheritdoc />
        public MarkErrorCode CreateCalibrationFile(
            string srcFile,
           string dstFile,
            double[] targetX,
            double[] targetY,
            double[] realX,
            double[] realY)
        {
    
            _logger.LogInformation(
                "正在创建校正文件: Src={SrcFile}, Dst={DstFile}, PointCount={Count}",
                srcFile, dstFile, targetX.Length);

            var result = _markController.CreateCalibrationFile(
                srcFile, dstFile, targetX, targetY, realX, realY);

            if (result == MarkErrorCode.None)
            {
                _logger.LogInformation("校正文件创建成功: {DstFile}", dstFile);
            }
            else
            {
                _logger.LogError("校正文件创建失败: {ErrorCode}", result);
            }

            return result;
        }
    }
}
