using System;
using System.Runtime.CompilerServices;

namespace NModbus.Data
{
    /// <summary>
    ///     Discriminated union for Modbus register values. Stored inline in collections — no boxing.
    ///     Size: 16 bytes (4 bytes type enum + 8 bytes primary value + 4 bytes padding).
    /// </summary>
    public readonly struct ModbusValue : IEquatable<ModbusValue>
    {
        private readonly DataTypeEnum _type;
        private readonly long _intVal;    // stores bool/short/ushort/int/uint/long
        private readonly ulong _uintVal;  // stores ulong
        private readonly double _dblVal;  // stores float/double

        private ModbusValue(DataTypeEnum type, long intVal, ulong uintVal, double dblVal)
        {
            _type = type;
            _intVal = intVal;
            _uintVal = uintVal;
            _dblVal = dblVal;
        }

        /// <summary>The data type of this value.</summary>
        public DataTypeEnum DataType => _type;

        /// <summary>True if this value has been assigned.</summary>
        public bool HasValue => _type != DataTypeEnum.None;

        #region Factory Methods

        public static ModbusValue From(bool value) => new(DataTypeEnum.Bool, value ? 1 : 0, 0, 0);
        public static ModbusValue From(short value) => new(DataTypeEnum.Int16, value, 0, 0);
        public static ModbusValue From(ushort value) => new(DataTypeEnum.UInt16, value, 0, 0);
        public static ModbusValue From(int value) => new(DataTypeEnum.Int32, value, 0, 0);
        public static ModbusValue From(uint value) => new(DataTypeEnum.UInt32, (long)value, 0, 0);
        public static ModbusValue From(long value) => new(DataTypeEnum.Int64, value, 0, 0);
        public static ModbusValue From(ulong value) => new(DataTypeEnum.UInt64, 0, value, 0);
        public static ModbusValue From(float value) => new(DataTypeEnum.Float, 0, 0, value);
        public static ModbusValue From(double value) => new(DataTypeEnum.Double, 0, 0, value);

        #endregion

        #region Type-Safe Getters (no boxing, no conversion)

        public bool ToBool()
        {
            if (_type != DataTypeEnum.Bool) ThrowTypeMismatch(DataTypeEnum.Bool);
            return _intVal != 0;
        }

        public short ToInt16()
        {
            if (_type != DataTypeEnum.Int16) ThrowTypeMismatch(DataTypeEnum.Int16);
            return (short)_intVal;
        }

        public ushort ToUInt16()
        {
            if (_type != DataTypeEnum.UInt16) ThrowTypeMismatch(DataTypeEnum.UInt16);
            return (ushort)_intVal;
        }

        public int ToInt32()
        {
            if (_type != DataTypeEnum.Int32) ThrowTypeMismatch(DataTypeEnum.Int32);
            return (int)_intVal;
        }

        public uint ToUInt32()
        {
            if (_type != DataTypeEnum.UInt32) ThrowTypeMismatch(DataTypeEnum.UInt32);
            return (uint)_intVal;
        }

        public long ToInt64()
        {
            if (_type != DataTypeEnum.Int64) ThrowTypeMismatch(DataTypeEnum.Int64);
            return _intVal;
        }

        public ulong ToUInt64()
        {
            if (_type != DataTypeEnum.UInt64) ThrowTypeMismatch(DataTypeEnum.UInt64);
            return _uintVal;
        }

        public float ToFloat()
        {
            if (_type != DataTypeEnum.Float) ThrowTypeMismatch(DataTypeEnum.Float);
            return (float)_dblVal;
        }

        public double ToDouble()
        {
            if (_type != DataTypeEnum.Double) ThrowTypeMismatch(DataTypeEnum.Double);
            return _dblVal;
        }

        #endregion

        #region Generic Conversion (no boxing for value types)

        /// <summary>
        ///     Convert to the requested type T. No boxing when T is a value type.
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
        ///     Each branch has explicit (object) cast to prevent numeric type promotion.
        /// </summary>
        public object ToObject() => _type switch
        {
            DataTypeEnum.None => throw new InvalidOperationException("ModbusValue is empty."),
            DataTypeEnum.Bool => (object)(_intVal != 0),
            DataTypeEnum.Int16 => (object)(short)_intVal,
            DataTypeEnum.UInt16 => (object)(ushort)_intVal,
            DataTypeEnum.Int32 => (object)(int)_intVal,
            DataTypeEnum.UInt32 => (object)(uint)_intVal,
            DataTypeEnum.Int64 => (object)_intVal,
            DataTypeEnum.UInt64 => (object)_uintVal,
            DataTypeEnum.Float => (object)(float)_dblVal,
            DataTypeEnum.Double => (object)_dblVal,
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

        public static implicit operator ModbusValue(bool v) => From(v);
        public static implicit operator ModbusValue(short v) => From(v);
        public static implicit operator ModbusValue(ushort v) => From(v);
        public static implicit operator ModbusValue(int v) => From(v);
        public static implicit operator ModbusValue(uint v) => From(v);
        public static implicit operator ModbusValue(long v) => From(v);
        public static implicit operator ModbusValue(ulong v) => From(v);
        public static implicit operator ModbusValue(float v) => From(v);
        public static implicit operator ModbusValue(double v) => From(v);

        public override string ToString() => HasValue ? $"{_type}: {ToObject()}" : "(empty)";

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTypeMismatch(DataTypeEnum expected) =>
            throw new InvalidOperationException($"Type mismatch: expected {expected}.");
    }
}
