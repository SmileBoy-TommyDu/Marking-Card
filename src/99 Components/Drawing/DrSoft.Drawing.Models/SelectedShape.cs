using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Model
{
    public class SelectedShape
    {
        public ShapeType SelectedShapeType { get; set; }= ShapeType.None;

        public bool IsLocked { get; set; }

    }
}
