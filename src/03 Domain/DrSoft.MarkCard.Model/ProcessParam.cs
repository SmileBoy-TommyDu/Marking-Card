

using System.Xml.Linq;

namespace DrSoft.MarkCard.Model
{
    /// <summary>
    /// 工艺参数
    /// </summary>
    public class ProcessParam
    {


        public bool Enable { get; set; }
        public double Power { get; set; } = 100;


        public double Frequency { get; set; } = 100;
        public double Pulse { get; set; } = 2;

        //是否调节激光参数
        public bool CanAdjustLaserParams { get; set; } = true;

        /// <summary>
        /// 循环打标次数
        /// </summary>
        public int RepeatCount { get; set; } = 1;
        /// <summary>
        /// 持续时间（微秒）
        /// </summary>
        public int DotDuration { get; set; } = 50;

        /// <summary>
        /// 打标速度 单位（mm/ms）
        /// </summary>
        public double MarkSpeed { get; set; }

        /// 跳转速度 单位（mm/ms）
        public double JumpSpeed { get; set; }

        /// <summary>
        /// 打标延时 单位（μs）
        /// </summary>
        public double MarkDelay { get; set; }

        /// <summary>
        /// 跳转延时 单位（μs）
        /// </summary>
        public double JumpDelay { get; set; }

        /// <summary>
        /// 多边形延时 单位（μs）
        /// </summary>
        public double PolyDelay { get; set; }

        /// <summary>
        /// 开光延时 单位（μs）
        /// </summary>
        public double LaserOnDelay { get; set; }

        /// <summary>
        /// 关光延时 单位（μs）
        /// </summary>
        public double LaserOffDelay { get; set; }


        public ProcessParam DeepCopy()
        {
            return new ProcessParam
            {
                Enable = this.Enable,
                   Power = this.Power ,
                   Frequency = this.Frequency ,
                   Pulse = this.Pulse ,
                   CanAdjustLaserParams = this.CanAdjustLaserParams ,
                   RepeatCount = this.RepeatCount ,
                   DotDuration = this.DotDuration ,
                   MarkSpeed = this.MarkSpeed ,
                   JumpSpeed = this.JumpSpeed ,
                   MarkDelay = this.MarkDelay ,
                   JumpDelay = this.JumpDelay ,
                   PolyDelay = this.PolyDelay ,
                   LaserOnDelay = this.LaserOnDelay ,
                   LaserOffDelay = this.LaserOffDelay
            };
        }

        //重写Equals方法
        public bool Equals(ProcessParam? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Enable == other.Enable &&
                   Power == other.Power &&
                   Frequency == other.Frequency &&
                   Pulse == other.Pulse &&
                   CanAdjustLaserParams == other.CanAdjustLaserParams &&
                   RepeatCount == other.RepeatCount &&
                   DotDuration == other.DotDuration &&
                   MarkSpeed == other.MarkSpeed &&
                   JumpSpeed == other.JumpSpeed &&
                   MarkDelay == other.MarkDelay &&
                   JumpDelay == other.JumpDelay &&
                   PolyDelay == other.PolyDelay &&
                   LaserOnDelay == other.LaserOnDelay &&
                   LaserOffDelay == other.LaserOffDelay;
        }

        //重新GetHashCode方法
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Enable);
            hash.Add(Power);
            hash.Add(Frequency);
            hash.Add(Pulse);
            hash.Add(CanAdjustLaserParams);
            hash.Add(RepeatCount);
            hash.Add(DotDuration);
            hash.Add(MarkSpeed);
            hash.Add(JumpSpeed);
            hash.Add(MarkDelay);
            hash.Add(JumpDelay);
            hash.Add(PolyDelay);
            hash.Add(LaserOnDelay);
            hash.Add(LaserOffDelay);
            return hash.ToHashCode();
        }




        // 重写 == 和 != 操作符
        public static bool operator ==(ProcessParam? left, ProcessParam? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(ProcessParam? left, ProcessParam? right)
        {
            return !(left == right);


        }
    }
}
