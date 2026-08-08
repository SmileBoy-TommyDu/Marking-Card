namespace DrSoft.Drawing.Controls.Interface;

/// <summary>
/// 画布交互内核向宿主上报状态和请求重绘的最小接口。
/// </summary>
public interface ICanvasStatusHost
{
    void UpdateStatus(string status);

    void Redraw();
}
