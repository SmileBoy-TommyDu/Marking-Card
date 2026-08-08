using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Model
{
    public interface IMultiCanvas
    {
        IReadOnlyList<ICanvas> CanvasCollection { get; }
        bool SwitchCanvas(ICanvas canvas);
        int CreateCanvas();
        int CloseSelectCanvas(int documentId);

    }
}
