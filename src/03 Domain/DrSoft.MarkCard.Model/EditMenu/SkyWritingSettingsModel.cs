namespace DrSoft.MarkCard.Model.EditMenu
{
    public record SkyWritingSettingsModel : ParameterBase, IMarkingParameter
    {
        private bool isEnabled;
        public bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        private uint skyWritingModel;

        /// <summary>
        /// SkyWriting 模式（0:关闭 1:标准 2:时间优化 3:智能切换）。
        /// 当 IsEnabled 为 true 时，保证返回值不为 0（默认为智能切换模式 3）。
        /// </summary>
        public uint SkyWritingModel
        {
            get => isEnabled && skyWritingModel == 0 ? (_extremeAngle>0?3u:2u) : skyWritingModel;
            set => skyWritingModel = value;
        }

        public double DelayTime { get; set; }  
        public int LaserOnDelay { get; set; }
        public int RunInTime { get; set; }
        public int RunOutTime { get; set; }

        private float _extremeAngle;

        //0到180度
        public float ExtremeAngle { get => _extremeAngle;
            
            set
            {
                if (_extremeAngle != value)
                {
                    _extremeAngle = value;
                   
                }
            }
        }
    }
}
