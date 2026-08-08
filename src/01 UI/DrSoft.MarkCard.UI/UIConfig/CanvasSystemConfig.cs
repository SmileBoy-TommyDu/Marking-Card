using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Parameter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.UI.UIConfig
{
    public class CanvasSystemConfig
    {
        public readonly static string Config_Path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "UIConfig.json");
        public DrawingBoardParameter DrawingBoardParameter { get; set; } = new DrawingBoardParameter();
        public SystemParam SystemParam { get; set; } = new SystemParam();


        public GalvoConfig  GalvoConfig { get; set; } = new GalvoConfig();

        public void SaveToFile()
        {
            if (this != null)
            {
                File.WriteAllText(Config_Path, JsonSerializer.Serialize<CanvasSystemConfig>(this));
            }
        }

    }
}
