using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.UI.Views.Parameter.Shape;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class DashSettingsViewModel : BaseParamViewModel<DashSettingParameter>
    {
        // 组数可选项 (1~10)
        public List<int> GroupCountOptions => Enumerable.Range(1, 10).ToList();

        // 当前选中组号的可选项（根据组数动态生成）
        public List<string> GroupNameOptions => Enumerable.Range(1, SelectedGroupCount).Select(i => "组-" + i).ToList();

        [ObservableProperty]
        private int _selectedGroupCount = 1;

        [ObservableProperty]
        private int _selectedGroupIndex = 0; // 0-based，对应组-1~组-N

        // 当前选中组的 A 值
        [ObservableProperty]
        private double _currentGroupA = 1.0;

        // 当前选中组的 B 值
        [ObservableProperty]
        private double _currentGroupB = 0.5;

        /// <summary>上一次选中的组号，用于切换时正确保存旧组数据</summary>
        private int _previousGroupIndex = 0;

        /// <summary>切换组时抑制 OnCurrentGroupXChanged 的自动同步</summary>
        private bool _isSwitchingGroup = false;

        public DashSettingsViewModel()
        {
            EventBus.Instance.Subscribe<CanvasChangedEvent>(async data =>
            {
                switch (data.ChangeType)
                {
                    case CanvasChangeType.Command:
                        await LoadParameterAsync();
                        break;
                    default:
                        break;
                }
            }, replayLast: true);
        }

        /// <summary>
        /// 弹框显示前设置 Content，避免在构造函数中创建 View 导致死循环
        /// </summary>
        protected override void OnPrepareForDialog()
        {
            Content = new DashSettingsView();
        }

        protected override Task BeforeApplyAsync(DashSettingParameter parameter)
        {
            // 同步当前编辑的 A/B 值到 Model
            SyncCurrentGroupToModel();

            return base.BeforeApplyAsync(parameter);
        }

        /// <summary>
        /// 组数变化时：更新组号选项、调整 DashGroups 列表大小、重置选中组号
        /// </summary>
        partial void OnSelectedGroupCountChanged(int value)
        {
            // 先保存当前组的数据到 Model（在列表可能被截断之前）
            if (!_isSwitchingGroup && _previousGroupIndex >= 0 && _previousGroupIndex < Model.DashGroups.Count)
            {
                Model.DashGroups[_previousGroupIndex] = new DashGroupData
                {
                    A = CurrentGroupA,
                    B = CurrentGroupB
                };
            }

            Model.GroupCount = value;

            // 确保 DashGroups 列表与组数匹配
            EnsureDashGroupsCount(value);

            // 重置组号为第一组
            _isSwitchingGroup = true;
            SelectedGroupIndex = 0;
            _isSwitchingGroup = false;

            _previousGroupIndex = 0;

            // 通知 GroupNameOptions 刷新
            OnPropertyChanged(nameof(GroupNameOptions));

            // 加载第一组数据
            LoadGroupData(0);
        }

        /// <summary>
        /// 组号变化时：保存旧组数据到 Model，加载新组数据
        /// </summary>
        partial void OnSelectedGroupIndexChanged(int value)
        {
            if (value < 0 || value >= SelectedGroupCount) return;

            // 保存之前的数据到 Model 的【旧组】索引
            if (!_isSwitchingGroup && _previousGroupIndex >= 0 && _previousGroupIndex < Model.DashGroups.Count)
            {
                Model.DashGroups[_previousGroupIndex] = new DashGroupData
                {
                    A = CurrentGroupA,
                    B = CurrentGroupB
                };
            }

            Model.SelectedGroupIndex = value;
            _previousGroupIndex = value;

            // 加载新组号的数据（抑制自动同步，避免写到错误的组）
            _isSwitchingGroup = true;
            LoadGroupData(value);
            _isSwitchingGroup = false;
        }

        /// <summary>
        /// 当前组 A 值变化时，实时同步到 Model
        /// </summary>
        partial void OnCurrentGroupAChanged(double value)
        {
            if (!_isSwitchingGroup)
                SyncCurrentGroupToModel();
        }

        /// <summary>
        /// 当前组 B 值变化时，实时同步到 Model
        /// </summary>
        partial void OnCurrentGroupBChanged(double value)
        {
            if (!_isSwitchingGroup)
                SyncCurrentGroupToModel();
        }

        /// <summary>
        /// 确保 DashGroups 列表数量与组数一致
        /// </summary>
        private void EnsureDashGroupsCount(int count)
        {
            while (Model.DashGroups.Count < count)
            {
                Model.DashGroups.Add(new DashGroupData());
            }
            while (Model.DashGroups.Count > count)
            {
                Model.DashGroups.RemoveAt(Model.DashGroups.Count - 1);
            }
        }

        /// <summary>
        /// 加载指定组号的 A/B 数据到编辑字段
        /// </summary>
        private void LoadGroupData(int groupIndex)
        {
            if (groupIndex >= 0 && groupIndex < Model.DashGroups.Count)
            {
                CurrentGroupA = Model.DashGroups[groupIndex].A;
                CurrentGroupB = Model.DashGroups[groupIndex].B;
            }
            else
            {
                CurrentGroupA = 1.0;
                CurrentGroupB = 0.5;
            }
        }

        /// <summary>
        /// 将当前编辑的 A/B 值同步回 Model 的 DashGroups
        /// </summary>
        private void SyncCurrentGroupToModel()
        {
            if (SelectedGroupIndex >= 0 && SelectedGroupIndex < Model.DashGroups.Count)
            {
                Model.DashGroups[SelectedGroupIndex] = new DashGroupData
                {
                    A = CurrentGroupA,
                    B = CurrentGroupB
                }; // record 不可变，需创建新实例
            }
        }

        public override async Task<DashSettingParameter> LoadParameterAsync()
        {
            var result = await base.LoadParameterAsync();

            // 从 Model 初始化组数和组号
            SelectedGroupCount = Model.GroupCount;
            EnsureDashGroupsCount(Model.GroupCount);

            if (Model.SelectedGroupIndex >= 0 && Model.SelectedGroupIndex < Model.GroupCount)
            {
                SelectedGroupIndex = Model.SelectedGroupIndex;
            }

            _previousGroupIndex = SelectedGroupIndex;

            _isSwitchingGroup = true;
            LoadGroupData(SelectedGroupIndex);
            _isSwitchingGroup = false;

            return result;
        }
    }
}
