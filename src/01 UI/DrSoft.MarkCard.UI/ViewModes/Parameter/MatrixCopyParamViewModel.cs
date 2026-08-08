using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class MatrixCopyParamViewModel : BaseParamViewModel<MatrixCopyParameter>
    {

        //public MatrixCopyParamViewModel()
        //{
        //    EventBus.Instance.Subscribe<ParaSaveEvent>(e => { if (e.ParaSaveType == ParaSaveType.Element && e.Trigger) _ = OnApplyAsync(); });
        //}
        // 模式
        [ObservableProperty]
        private CopyMode _selectedMode = CopyMode.None;

        // 平均分布开关：独立 observable，驱动"间隔角度"控件的 IsEnabled 状态
        [ObservableProperty]
        private bool _isAverageDistribute = false;



        // 映射 UI Index 到 Enum
        public int SelectedModeIndex
        {
            get => (int)SelectedMode;
            set
            {

                SelectedMode = (CopyMode)value;
                Model.Mode = SelectedMode;

            }
        }

        protected override Task ExecuteApplyAsync()
        {
            switch (Model.Mode)
            {
                case CopyMode.None:
                    break;
                case CopyMode.Matrix:
                    _drawingService.Shapes.MatrixCopy(Model.ColumnCount, Model.ColumnSpacing, Model.RowCount, Model.RowSpacing);
                    break;
                case CopyMode.Circular:
                    _drawingService.Shapes.CircleCopy(Model.Radius, Model.Count, Model.StartAngle, Model.IntervalAngle,
                        IsAverageDistribute, Model.IsObjectRotate, Model.IsCounterClockwise);
                    break;
            }
            return Task.CompletedTask;
        }

        public override async Task<MatrixCopyParameter> LoadParameterAsync()
        {
            // 重置复制模式为"无"
            SelectedMode = CopyMode.None;
            // 从 Model 恢复 UI 状态
            Model = new MatrixCopyParameter();
            OnPropertyChanged();
            return Model;
        }
    }
}
