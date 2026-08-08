using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Model;
using System.Collections.ObjectModel;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 删除图层命令（支持撤销）：记录被删除的图层及其原始位置，撤销时恢复到原位。
    /// </summary>
    internal class CommandRemoveLayer : IDrawingCommand
    {
        private readonly ObservableCollection<LayerViewModel> _collection;
        private readonly List<(int Index, LayerViewModel Layer)> _removedLayers;

        public string Description => $"删除 {_removedLayers.Count} 个图层";

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="collection">图层所在的 ObservableCollection</param>
        /// <param name="layers">要删除的图层列表</param>
        public CommandRemoveLayer(ObservableCollection<LayerViewModel> collection, IEnumerable<LayerViewModel> layers)
        {
            _collection = collection;
            _removedLayers = new List<(int, LayerViewModel)>();

            // 记录每个图层在集合中的原始索引，用于撤销时恢复位置
            foreach (var layer in layers)
            {
                int index = collection.IndexOf(layer);
                if (index >= 0)
                    _removedLayers.Add((index, layer));
            }

            // 按索引排序，确保 Execute 时从后往前删除不影响前面的索引
            _removedLayers.Sort((a, b) => b.Index.CompareTo(a.Index));
        }

        public void Execute()
        {
            // 从后往前删除，避免索引偏移
            foreach (var (_, layer) in _removedLayers)
            {
                _collection.Remove(layer);
            }
        }

        public bool Undo()
        {
            // 按原始索引恢复（_removedLayers 已按 Index 降序排列，撤销时需按升序插入）
            foreach (var (index, layer) in _removedLayers.OrderBy(x => x.Index))
            {
                if (index <= _collection.Count)
                    _collection.Insert(index, layer);
                else
                    _collection.Add(layer);
            }

            // 恢复选中状态
            foreach (var (_, layer) in _removedLayers)
            {
                layer.IsSelected = true;
            }
            return true;
        }
    }
}
