using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Parameter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Service
{
    public class SystemParaForGalvoService
    {
        public SystemParaForGalvoService()
        {
            _Param = new GalvoConfig();
        }
        private GalvoConfig? _Param;
        public void BindGalvoParas(GalvoConfig param)
        {
            _Param = param;
        }

        public GalvoConfig? GetGalvoParas()
        {
            return _Param;
        }
    }
}
