using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using System;

namespace DrSoft.MarkCard.Interface
{
    public interface IPowerMeter : IDisposable
    {
        event Action<string> FeedbackValueReceived;

        bool IsConnected { get; }

        MarkErrorCode Connect(PowerMeterConfig config);

        MarkErrorCode Disconnect();
    }
}
