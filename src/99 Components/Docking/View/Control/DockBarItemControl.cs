using DrSoft.Docking.Interface;
using DrSoft.Docking.Model.Element;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrSoft.Docking.Enum;

namespace DrSoft.Docking.View
{
    public class DockBarItemControl : ContentControl, IDockView
    {
        static DockBarItemControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DockBarItemControl), new FrameworkPropertyMetadata(typeof(DockBarItemControl)));
        }

        internal DockBarItemControl(IDockView dockViewParent)
        {
            _dockViewParent = dockViewParent;
        }

        public ILayoutGroup Container
        {
            get
            {
                return _dockViewParent?.Model as ILayoutGroup;
            }
        }

        public IDockModel Model
        {
            get
            {
                return null;
            }
        }

        private IDockView _dockViewParent;
        public IDockView DockViewParent
        {
            get
            {
                return _dockViewParent;
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var ele = Content as DockElement;
            if (ele == Container.DockManager.AutoHideElement)
                Container.ShowWithActive(null);
            else Container.ShowWithActive(ele);
            base.OnMouseLeftButtonDown(e);
        }

        public void Dispose()
        {
            _dockViewParent = null;
        }
    }
}