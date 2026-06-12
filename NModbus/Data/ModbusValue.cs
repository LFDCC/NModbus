using System;
using System.Runtime.CompilerServices;

namespace NModbus.Data
{
    /// <summary>
    ///     Discriminated union for Modbus register values. Stored inline in collections — no boxing.
    ///     Size: 12 bytes (1 byte type tag + up to 8 bytes value + padding).
    /// </summary>
    public readonly struct ModbusValue : IEquatable<ModbusValue>
    {
        private readonly byte _type;  // 0=empty, 1=bool, 2=short, 3=ushort, 4=int, 5=uint, 6=long, 7=ulong, 8=float, 9=double
        private readonly long _intVal;    // stores int/short/ushort/int/uint/long (sign-extended)
        private readonly ulong _uintVal;  // stores ulong
        private readonly double _dblVal;  // stores float/double

        private ModbusValue(byte type, long intVal, ulong uintVal, double dblVal)
        {
            _type = type;
            _intVal = intVal;
            _uintVal = uintVal;
            _dblVal = dblVal;
        }

        /// <summary>The data type of this value.</summary>
        public DataTypeEnum DataType => _type switch
        {
            1 => DataTypeEnum.Bool,
            2 => DataTypeEnum.Int16,
            3 => DataTypeEnum.UInt16,
            4 => DataTypeEnum.Int32,
            5 => DataTypeEnum.UInt32,
            6 => DataTypeEnum.Int64,
            7 => DataTypeEnum.UInt64,
            8 => DataTypeEnum.Float,
            9 => DataTypeEnum.Double,
            _ => throw new InvalidOperationException("ModbusValue is empty.")
        };

        /// <summary>True if this value has been assigned.</summary>
        public bool HasValue => _type != 0;

        #region Factory Methods

        public static ModbusValue From(bool value) => new(1, value ? 1 : 0, 0, 0);
        public static ModbusValue From(short value) => new(2, value, 0, 0);
        public static ModbusValue From(ushort value) => new(3, value, 0, 0);
        public static ModbusValue From(int value) => new(4, value, 0, 0);
        public static ModbusValue From(uint value) => new(5, (long)value, 0, 0);
        public static ModbusValue From(long value) => new(6, value, 0, 0);
        public static ModbusValue From(ulong value) => new(7, 0, value, 0);
        public static ModbusValue From(float value) => new(8, 0, 0, value);
        public static ModbusValue From(double value) => new(9, 0, 0, value);

        #endregion

        #region Type-Safe Getters (no boxing, no conversion)

        public bool ToBool()
        {
            if (_type != 1) ThrowTypeMismatch(DataTypeEnum.Bool);
            return _intVal != 0;
        }

        public short ToInt16()
        {
            if (_type != 2) ThrowTypeMismatch(DataTypeEnum.Int16);
            return (short)_intVal;
        }

        public ushort ToUInt16()
        {
            if (_type != 3) ThrowTypeMismatch(DataTypeEnum.UInt16);
            return (ushort)_intVal;
        }

        public int ToInt32()
        {
            if (_type != 4) ThrowTypeMismatch(DataTypeEnum.Int32);
            return (int)_intVal;
        }

        public uint ToUInt32()
        {
            if (_type != 5) ThrowTypeMismatch(DataTypeEnum.UInt32);
            return (uint)_intVal;
        }

        public long ToInt64()
        {
            if (_type != 6) ThrowTypeMismatch(DataTypeEnum.Int64);
            return _intVal;
        }

        public ulong ToUInt64()
        {
            if (_type != 7) ThrowTypeMismatch(DataTypeEnum.UInt64);
            return _uintVal;
        }

        public float ToFloat()
        {
            if (_type != 8) ThrowTypeMismatch(DataTypeEnum.Float);
            return (float)_dblVal;
        }

        public double ToDouble()
        {
            if (_type != 9) ThrowTypeMismatch(DataTypeEnum.Double);
            return _dblVal;
        }

        #endregion

        #region Generic Conversion (still no boxing for value types)

        /// <summary>
        ///     Convert to the requested type T. Supports all Modbus data types.
        ///     No boxing when T is a value type (JIT eliminates the cast for same-type).
        /// </summary>
        public T To<T>()
        {
            var t = typeof(T);
            if (t == typeof(bool))   { bool v = ToBool();       return Unsafe.As<bool, T>(ref v); }
            if (t == typeof(short))  { short v = ToInt16();     return Unsafe.As<short, T>(ref v); }
            if (t == typeof(ushort)) { ushort v = ToUInt16();   return Unsafe.As<ushort, T>(ref v); }
            if (t == typeof(int))    { int v = ToInt32();       return Unsafe.As<int, T>(ref v); }
            if (t == typeof(uint))   { uint v = ToUInt32();     return Unsafe.As<uint, T>(ref v); }
            if (t == typeof(long))   { long v = ToInt64();      return Unsafe.As<long, T>(ref v); }
            if (t == typeof(ulong))  { ulong v = ToUInt64();    return Unsafe.As<ulong, T>(ref v); }
            if (t == typeof(float))  { float v = ToFloat();     return Unsafe.As<float, T>(ref v); }
            if (t == typeof(double)) { double v = ToDouble();   return Unsafe.As<double, T>(ref v); }
            throw new NotSupportedException($"Type {t.Name} is not supported.");
        }

        #endregion

        #region ToObject (boxing, for backward compatibility)

        /// <summary>
        ///     Convert to object. This boxes the value — use To&lt;T&gt;() for zero-allocation.
        /// </summary>
        public object ToObject() => _type switch
        {
            1 => _intVal != 0,
            2 => (short)_intVal,
            3 => (ushort)_intVal,
            4 => (int)_intVal,
            5 => (uint)_intVal,
            6 => _intVal,
            7 => _uintVal,
            8 => (float)_dblVal,
            9 => _dblVal,
            _ => throw new InvalidOperationException("ModbusValue is empty.")
        };

        #endregion

        #region Equality

        public bool Equals(ModbusValue other) =>
            _type == other._type && _intVal == other._intVal && _uintVal == other._uintVal && _dblVal == other._dblVal;

        public override bool Equals(object obj) => obj is ModbusValue other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_type, _intVal, _uintVal, _dblVal);

        public static bool operator ==(ModbusValue left, ModbusValue right) => left.Equals(right);
        public static bool operator !=(ModbusValue left, ModbusValue right) => !left.Equals(right);

        #endregion

        public override string ToString() => HasValue ? $"{DataType}: {ToObject()}" : "(empty)";

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTypeMismatch(DataTypeEnum expected) =>
            throw new InvalidOperationException($"Type mismatch: expected {expected}.");
    }
}
