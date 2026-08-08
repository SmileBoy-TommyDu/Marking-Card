using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class LayerInputIOViewModel : ObservableObject
    {
        /// <summary>输入状态勾选集合（1~16）</summary>
        [ObservableProperty] private ObservableCollection<IOCheckItem> _inputList;

        /// <summary>逾期时间(ms)，-1 表示无限等待</summary>
        [ObservableProperty] private int _timeoutMs = 10;

        /// <summary>true:等待图层输入 false:匹配图层输入</summary>
        [ObservableProperty] private bool _isWaitLayerInput = true;

        public LayerInputIOViewModel()
        {
            InputList = new ObservableCollection<IOCheckItem>();
            for (int i = 1; i <= 16; i++)
                InputList.Add(new IOCheckItem(i));
        }

        [RelayCommand]
        private void Apply()
        {
            // TODO: 将输入配置套用到当前页面
        }
    }
}
