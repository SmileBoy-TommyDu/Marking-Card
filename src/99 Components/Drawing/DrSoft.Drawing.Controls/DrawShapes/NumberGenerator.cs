namespace DrSoft.Drawing.Controls.DrawShapes
{

    public class UniqueIdGenerator
    {
        private static readonly long EpochTicks = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private static int _count = CreateInitialCount();

        private static int CreateInitialCount()
        {
            long currentTicks = DateTime.UtcNow.Ticks;
            return (int)((currentTicks - EpochTicks) / 10000000);
        }

        public static int NextId()
        {
            return Interlocked.Increment(ref _count);
        }
    }

    public class SerialNumberGenerator
    {
        public SerialNumberGenerator(int initVal = 0)
        {
            _current = initVal;
        }
        private int _current = 0;

        public int NextId()
            => Interlocked.Increment(ref _current);

        /// <summary>
        /// 将计数器重置为指定值之后（用于从快照加载后跳过已有序号）
        /// </summary>
        public void ResetToAtLeast(int maxValue)
        {
            if (maxValue > _current)
                Interlocked.Exchange(ref _current, maxValue);
        }
    }
}
