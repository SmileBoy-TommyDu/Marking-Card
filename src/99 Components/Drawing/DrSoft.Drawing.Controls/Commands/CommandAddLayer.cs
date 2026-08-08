using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Model;
using System.Collections.ObjectModel;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 添加图层命令（支持撤销）：Execute 添加图层到集合，Undo 移除已添加的图层。
    /// </summary>
    internal class CommandAddLayer : IDrawingCommand
    {
        private readonly ObservableCollection<LayerViewModel> _collection;
        private readonly List<LayerViewModel> _addedLayers;

        public string Description => $"添加 {_addedLayers.Count} 个图层";

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="collection">图层所在的 ObservableCollection</param>
        /// <param name="layers">要添加的图层列表</param>
        public CommandAddLayer(ObservableCollection<LayerViewModel> collection, IEnumerable<LayerViewModel> layers)
        {
            _collection = collection;
            _addedLayers = layers.ToList();
        }

        public void Execute()
        {
            foreach (var layer in _addedLayers)
            {
                _collection.Add(layer);
            }
        }

        public bool Undo()
        {
            // 如果当前只剩下最后一个图层，则不允许撤销
            if (_collection.Count <= 1)
            {
                MessageBox.Show("至少需要一个图层！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // 从后往前移除，避免索引偏移
            for (int i = _addedLayers.Count - 1; i >= 0; i--)
            {
                _collection.Remove(_addedLayers[i]);
            }
            return true;
        }
    }
}
