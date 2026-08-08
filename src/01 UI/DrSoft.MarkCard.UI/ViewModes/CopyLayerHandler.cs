using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes
{
    /// <summary>
    /// 复制图层事件的 UI 层处理器：
    /// 1. 订阅 CopyLayerCountRequestEvent，弹出输入对话框获取复制数量
    /// 2. 订阅 CopyLayerParametersEvent，将原图形绑定的加工参数复制到新图形上
    /// </summary>
    public class CopyLayerHandler
    {
        private readonly IMarkingParam _markingParam;

        public CopyLayerHandler(IMarkingParam markingParam)
        {
            _markingParam = markingParam;
            EventBus.Instance.Subscribe<CopyLayerCountRequestEvent, int?>(OnCopyLayerCountRequest);
            EventBus.Instance.Subscribe<CopyLayerParametersEvent>(OnCopyLayerParameters);
        }

        /// <summary>
        /// 处理复制数量请求：弹出输入对话框，返回用户输入的数量
        /// </summary>
        private int? OnCopyLayerCountRequest(CopyLayerCountRequestEvent e)
        {
            // 默认复制数量为 1，不弹窗提示
            return 1;
            // int? result = null;
            // Application.Current.Dispatcher.Invoke(() =>
            // {
            //     var dialog = new CopyLayerCountDialog(e.LayerName) { Owner = Application.Current.MainWindow };
            //     if (dialog.ShowDialog() == true)
            //         result = dialog.CopyCount;
            // });
            // return result;
        }

        /// <summary>
        /// 处理参数复制：将原图形绑定的加工参数复制到新图形上
        /// </summary>
        private void OnCopyLayerParameters(CopyLayerParametersEvent e)
        {
            var canvasParams = _markingParam.GetParameters(e.CanvasId);
            if (canvasParams == null || canvasParams.Count == 0) return;

            var newBindings = new Dictionary<int, IList<ParameterBase>>();

            foreach (var kvp in e.OldToNewUIdMap)
            {
                int oldUId = kvp.Key;
                int newUId = kvp.Value;

                if (canvasParams.TryGetValue(oldUId, out var parameters))
                {
                    // 深拷贝参数列表（ParameterBase 是 record，with 表达式创建副本）
                    var clonedParams = new List<ParameterBase>();
                    foreach (var param in parameters)
                        clonedParams.Add(param with { });

                    newBindings[newUId] = clonedParams;
                }
            }

            // 批量绑定新参数
            if (newBindings.Count > 0)
            {
                _markingParam.SetParameters(e.CanvasId, newBindings);
            }
        }
    }
}
