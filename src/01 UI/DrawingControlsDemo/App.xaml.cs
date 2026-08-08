using DrSoft.Drawing.Registration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace DrawingControlsDemo
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            services.RegisterDrawingTools();
            Services = services.BuildServiceProvider();
        }
    }
}