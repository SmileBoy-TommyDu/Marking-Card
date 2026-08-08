using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public class MarkBitmapCommand:IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.MarkBitmapCommand;

        public byte[]? BitmapData { get; set; }
    
    }
}
