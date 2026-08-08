using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public interface IMarkCommand
    {
        MarkCommandType MarkCommandType {  get; }


    }
}
