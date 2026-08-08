﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class LayerOutputIOViewModel : ObservableObject
    {
        /// <summary>输出状态勾选集合（1~16）</summary>
        [ObservableProperty] private ObservableCollection<IOCheckItem> _outputList;

        /// <summary>触发时机（0:加工前 1:加工后）</summary>
        [ObservableProperty] private int _triggerTimingIndex = 1;

        /// <summary>自动清除信号</summary>
        [ObservableProperty] private bool _autoClearSignal = true;

        public LayerOutputIOViewModel()
        {
            OutputList = new ObservableCollection<IOCheckItem>();
            for (int i = 1; i <= 16; i++)
                OutputList.Add(new IOCheckItem(i));
        }

        [RelayCommand]
        private void Apply()
        {
            // TODO: 将输出配置套用到当前页面
        }
    }

    /// <summary>
    /// 图层输入/输出 IO 勾选项，供 ItemsControl 绑定使用。
    /// </summary>
    public partial class IOCheckItem : ObservableObject
    {
        public IOCheckItem(int index, bool isChecked = false, bool isUsed = true)
        {
            _index = index;
            _isChecked = isChecked;
            _isUsed = isUsed;
        }

        /// <summary>序号（1~16）</summary>
        [ObservableProperty] private int _index;

        /// <summary>是否勾选</summary>
        [ObservableProperty] private bool _isChecked;

        /// <summary>是否已被使用</summary>
        [ObservableProperty] private bool _isUsed;
    }
}
