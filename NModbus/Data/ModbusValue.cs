using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NModbus.Data
{
    /// <summary>
    /// Modbus 值的联合类型。真正的联合体（16 字节，栈分配）。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public readonly struct ModbusValue : IEquatable<ModbusValue>
    {
        // === 类型判别器（1 字节，DataTypeEnum : byte） ===
        [FieldOffset(0)] private readonly DataTypeEnum _type;

        // === 值联合体（8 字节，互斥共享） ===
        [FieldOffset(8)] private readonly long _intVal;    // bool / short / ushort / int / uint / long
        [FieldOffset(8)] private readonly ulong _ulongVal;  // ulong
        [FieldOffset(8)] private readonly double _dblVal;    // float / double

        // === 公开属性 ===
        public DataTypeEnum DataType => _type;

        /// <summary>显式的空值实例。</summary>
        public static ModbusValue None => default;

        // === 类型判断（零开销，直接比较判别器） ===
        public bool IsBool => _type == DataTypeEnum.Bool;
        public bool IsInt16 => _type == DataTypeEnum.Int16;
        public bool IsUInt16 => _type == DataTypeEnum.UInt16;
        public bool IsInt32 => _type == DataTypeEnum.Int32;
        public bool IsUInt32 => _type == DataTypeEnum.UInt32;
        public bool IsInt64 => _type == DataTypeEnum.Int64;
        public bool IsUInt64 => _type == DataTypeEnum.UInt64;
        public bool IsFloat => _type == DataTypeEnum.Float;
        public bool IsDouble => _type == DataTypeEnum.Double;

        // ========================================================================
        //  构造函数（私有，三个重载各自只写对应的字段，避免覆盖联合体内存）
        // ========================================================================

        /// <summary>适用于 long 族：bool / short / ushort / int / uint / long</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ModbusValue(DataTypeEnum type, long intVal) : this()
        {
            _type = type;
            _intVal = intVal;
        }

        /// <summary>适用于 ulong</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ModbusValue(DataTypeEnum type, ulong ulongVal) : this()
        {
            _type = type;
            _ulongVal = ulongVal;
        }

        /// <summary>适用于 float / double</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ModbusValue(DataTypeEnum type, double dblVal) : this()
        {
            _type = type;
            _dblVal = dblVal;
        }

        // ========================================================================
        //  工厂方法
        // ========================================================================

        public static ModbusValue From(bool value) =>
            new(DataTypeEnum.Bool, value ? 1L : 0L);

        public static ModbusValue From(short value) =>
            new(DataTypeEnum.Int16, (long)value);

        public static ModbusValue From(ushort value) =>
            new(DataTypeEnum.UInt16, (long)value);

        public static ModbusValue From(int value) =>
            new(DataTypeEnum.Int32, (long)value);

        /// <summary>
        /// uint 存入 _intVal（long），高 32 位始终为零，读回时截断无损。
        /// </summary>
        public static ModbusValue From(uint value) =>
            new(DataTypeEnum.UInt32, (long)value);

        public static ModbusValue From(long value) =>
            new(DataTypeEnum.Int64, value);

        public static ModbusValue From(ulong value) =>
            new(DataTypeEnum.UInt64, value);

        /// <summary>float 通过隐式提升为 double 存储（无损）。</summary>
        public static ModbusValue From(float value) =>
            new(DataTypeEnum.Float, (double)value);

        public static ModbusValue From(double value) =>
            new(DataTypeEnum.Double, value);

        // ========================================================================
        //  类型安全的取值方法（类型不匹配时抛异常）
        // ========================================================================

        public bool ToBool()
        {
            if (!IsBool) ThrowTypeMismatch(DataTypeEnum.Bool);
            return _intVal != 0;
        }

        public short ToInt16()
        {
            if (!IsInt16) ThrowTypeMismatch(DataTypeEnum.Int16);
            return (short)_intVal;
        }

        public ushort ToUInt16()
        {
            if (!IsUInt16) ThrowTypeMismatch(DataTypeEnum.UInt16);
            return (ushort)_intVal;
        }

        public int ToInt32()
        {
            if (!IsInt32) ThrowTypeMismatch(DataTypeEnum.Int32);
            return (int)_intVal;
        }

        public uint ToUInt32()
        {
            if (!IsUInt32) ThrowTypeMismatch(DataTypeEnum.UInt32);
            return (uint)_intVal;
        }

        public long ToInt64()
        {
            if (!IsInt64) ThrowTypeMismatch(DataTypeEnum.Int64);
            return _intVal;
        }

        public ulong ToUInt64()
        {
            if (!IsUInt64) ThrowTypeMismatch(DataTypeEnum.UInt64);
            return _ulongVal;
        }

        public float ToFloat()
        {
            if (!IsFloat) ThrowTypeMismatch(DataTypeEnum.Float);
            return (float)_dblVal;
        }

        public double ToDouble()
        {
            if (!IsDouble) ThrowTypeMismatch(DataTypeEnum.Double);
            return _dblVal;
        }

        // ========================================================================
        //  安全取值（不抛异常）
        // ========================================================================

        public bool TryGetBool(out bool value)
        {
            if (IsBool) { value = _intVal != 0; return true; }
            value = default; return false;
        }

        public bool TryGetInt16(out short value)
        {
            if (IsInt16) { value = (short)_intVal; return true; }
            value = default; return false;
        }

        public bool TryGetUInt16(out ushort value)
        {
            if (IsUInt16) { value = (ushort)_intVal; return true; }
            value = default; return false;
        }

        public bool TryGetInt32(out int value)
        {
            if (IsInt32) { value = (int)_intVal; return true; }
            value = default; return false;
        }

        public bool TryGetUInt32(out uint value)
        {
            if (IsUInt32) { value = (uint)_intVal; return true; }
            value = default; return false;
        }

        public bool TryGetInt64(out long value)
        {
            if (IsInt64) { value = _intVal; return true; }
            value = default; return false;
        }

        public bool TryGetUInt64(out ulong value)
        {
            if (IsUInt64) { value = _ulongVal; return true; }
            value = default; return false;
        }

        public bool TryGetFloat(out float value)
        {
            if (IsFloat) { value = (float)_dblVal; return true; }
            value = default; return false;
        }

        public bool TryGetDouble(out double value)
        {
            if (IsDouble) { value = _dblVal; return true; }
            value = default; return false;
        }

        // ========================================================================
        //  泛型取值（零装箱，Unsafe.As 消除 (T)(object) 的堆分配）
        // ========================================================================

        /// <summary>
        /// 将存储值转为指定类型 T（零装箱）。
        /// T 必须是 bool / short / ushort / int / uint / long / ulong / float / double 之一。
        /// </summary>
        public T To<T>()
        {
            if (typeof(T) == typeof(bool))    return As<bool, T>(_intVal != 0);
            if (typeof(T) == typeof(short))   return As<short, T>((short)_intVal);
            if (typeof(T) == typeof(ushort))  return As<ushort, T>((ushort)_intVal);
            if (typeof(T) == typeof(int))     return As<int, T>((int)_intVal);
            if (typeof(T) == typeof(uint))    return As<uint, T>((uint)_intVal);
            if (typeof(T) == typeof(long))    return As<long, T>(_intVal);
            if (typeof(T) == typeof(ulong))   return As<ulong, T>(_ulongVal);
            if (typeof(T) == typeof(float))   return As<float, T>((float)_dblVal);
            if (typeof(T) == typeof(double))  return As<double, T>(_dblVal);
            throw new NotSupportedException($"不支持的类型: {typeof(T).Name}");
        }

        /// <summary>
        /// 零装箱类型重解释辅助。调用方通过 <c>typeof(T) == typeof(TFrom)</c>
        /// 证明了 TFrom 与 T 是同一运行时类型，reinterpret 为恒等变换。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TTo As<TFrom, TTo>(TFrom value)
        {
            return Unsafe.As<TFrom, TTo>(ref value);
        }

        // ========================================================================
        //  模式匹配
        // ========================================================================

        /// <summary>带返回值的模式匹配。</summary>
        public T Match<T>(
            Func<bool, T> @bool,
            Func<short, T> @short,
            Func<ushort, T> @ushort,
            Func<int, T> @int,
            Func<uint, T> @uint,
            Func<long, T> @long,
            Func<ulong, T> @ulong,
            Func<float, T> @float,
            Func<double, T> @double)
        {
            return _type switch
            {
                DataTypeEnum.Bool => @bool(ToBool()),
                DataTypeEnum.Int16 => @short(ToInt16()),
                DataTypeEnum.UInt16 => @ushort(ToUInt16()),
                DataTypeEnum.Int32 => @int(ToInt32()),
                DataTypeEnum.UInt32 => @uint(ToUInt32()),
                DataTypeEnum.Int64 => @long(ToInt64()),
                DataTypeEnum.UInt64 => @ulong(ToUInt64()),
                DataTypeEnum.Float => @float(ToFloat()),
                DataTypeEnum.Double => @double(ToDouble()),
                _ => throw new InvalidOperationException($"无效的类型: {_type}")
            };
        }

        /// <summary>无返回值的模式匹配（副作用操作）。</summary>
        public void Match(
            Action<bool> @bool,
            Action<short> @short,
            Action<ushort> @ushort,
            Action<int> @int,
            Action<uint> @uint,
            Action<long> @long,
            Action<ulong> @ulong,
            Action<float> @float,
            Action<double> @double)
        {
            switch (_type)
            {
                case DataTypeEnum.Bool: @bool(ToBool()); break;
                case DataTypeEnum.Int16: @short(ToInt16()); break;
                case DataTypeEnum.UInt16: @ushort(ToUInt16()); break;
                case DataTypeEnum.Int32: @int(ToInt32()); break;
                case DataTypeEnum.UInt32: @uint(ToUInt32()); break;
                case DataTypeEnum.Int64: @long(ToInt64()); break;
                case DataTypeEnum.UInt64: @ulong(ToUInt64()); break;
                case DataTypeEnum.Float: @float(ToFloat()); break;
                case DataTypeEnum.Double: @double(ToDouble()); break;
                default: throw new InvalidOperationException($"无效的类型: {_type}");
            }
        }

        // ========================================================================
        //  相等性（无装箱）
        // ========================================================================

        public bool Equals(ModbusValue other)
        {
            if (_type != other._type) return false;

            return _type switch
            {
                // long 族：bool / short / ushort / int / uint / long 都存在 _intVal 中
                DataTypeEnum.Bool or DataTypeEnum.Int16 or DataTypeEnum.UInt16 or
                DataTypeEnum.Int32 or DataTypeEnum.UInt32 or DataTypeEnum.Int64
                    => _intVal == other._intVal,

                DataTypeEnum.UInt64
                    => _ulongVal == other._ulongVal,

                // 浮点族：float 提升为 double 是无损的，但 NaN 需要用位级比较
                DataTypeEnum.Float or DataTypeEnum.Double
                    => BitConverter.DoubleToInt64Bits(_dblVal) ==
                       BitConverter.DoubleToInt64Bits(other._dblVal),

                _ => true  // DataTypeEnum.None
            };
        }

        public override bool Equals(object obj) => obj is ModbusValue other && Equals(other);

        public static bool operator ==(ModbusValue left, ModbusValue right) => left.Equals(right);
        public static bool operator !=(ModbusValue left, ModbusValue right) => !left.Equals(right);

        public override int GetHashCode()
        {
            // float / double：使用位级哈希，保证 NaN 的一致性
            if (IsFloat || IsDouble)
                return HashCode.Combine(_type, BitConverter.DoubleToInt64Bits(_dblVal));

            // UInt64：直接读 _ulongVal
            if (IsUInt64)
                return HashCode.Combine(_type, _ulongVal);

            // 其余类型（Bool/Int16/UInt16/Int32/UInt32/Int64）：
            // 值均存储在 _intVal 中，直接读取位模式
            return HashCode.Combine(_type, _intVal);
        }

        // ========================================================================
        //  转换为 object（装箱，用于向后兼容场景）
        // ========================================================================

        public object ToObject()
        {
            return _type switch
            {
                DataTypeEnum.Bool => (object)ToBool(),
                DataTypeEnum.Int16 => (object)ToInt16(),
                DataTypeEnum.UInt16 => (object)ToUInt16(),
                DataTypeEnum.Int32 => (object)ToInt32(),
                DataTypeEnum.UInt32 => (object)ToUInt32(),
                DataTypeEnum.Int64 => (object)ToInt64(),
                DataTypeEnum.UInt64 => (object)ToUInt64(),
                DataTypeEnum.Float => (object)ToFloat(),
                DataTypeEnum.Double => (object)ToDouble(),
                _ => throw new InvalidOperationException($"无效的类型: {_type}")
            };
        }

        public override string ToString() => $"{_type}: {ToObject()}";

        // ========================================================================
        //  内部辅助
        // ========================================================================

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowTypeMismatch(DataTypeEnum expected) =>
            throw new InvalidOperationException(
                $"类型不匹配: 期望 {expected}，实际存储 {_type}。");
    }
}
