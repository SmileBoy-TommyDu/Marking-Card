using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace DrSoft.Drawing.Utility
{
    public static class EnumExtensions
    {
        // 每个枚举类型对应一个 value→description 的字典
        private static readonly ConcurrentDictionary<Type, Dictionary<int, string>> _cache = new();

        public static string GetDescription(this Enum value)
        {
            var type = value.GetType();
            var intVal = Convert.ToInt32(value);

            // 第一次访问该枚举类型时，反射一次性构建字典
            var map = _cache.GetOrAdd(type, BuildMap);

            return map.TryGetValue(intVal, out var desc) ? desc : value.ToString();
        }

        private static Dictionary<int, string> BuildMap(Type enumType)
        {
            var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            var map = new Dictionary<int, string>(fields.Length);

            foreach (var field in fields)
            {
                var intVal = Convert.ToInt32(field.GetValue(null));
                var attr = field.GetCustomAttribute<DescriptionAttribute>();
                map[intVal] = attr?.Description ?? field.Name;
            }

            return map;
        }
    }
}
