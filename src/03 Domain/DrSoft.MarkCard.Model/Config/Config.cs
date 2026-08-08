using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Config
{
    public class Config
    {
        public readonly static string Config_Path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");
        public SystemConfig SystemConfig { get; set; } = new SystemConfig();

        public List<CardConfig> CardConfigs { get; set; } = new List<CardConfig>();

        ///// <summary>
        ///// 第一张卡的配置（向后兼容）
        ///// </summary>
        //public CardConfig CardConfig => CardConfigs.FirstOrDefault();

        public List<ScanHeadConfig> ScanHeadConfigs { get; set; } = new List<ScanHeadConfig>();

        public List<LaserConfig> LaserConfigs { get; set; } = new List<LaserConfig>();

        public PowerMeterConfig PowerMeterConfig { get; set; } = new PowerMeterConfig();

        public List<IOConfig> IOConfigs { get; set; } = new List<IOConfig>();


        public void SaveToFile()
        {
            if (this != null)
            {
                File.WriteAllText(Config_Path, JsonSerializer.Serialize<Config>(this));
            }
        }

    }
}
