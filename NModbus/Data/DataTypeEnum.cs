namespace NModbus.Data
{
    /// <summary>
    ///     Supported data types for Modbus register interpretation.
    /// </summary>
    public enum DataTypeEnum : byte
    {
        /// <summary>Boolean (coil/discrete input, 1 bit)</summary>
        Bool,

        /// <summary>Signed 16-bit integer (1 register)</summary>
        Int16,

        /// <summary>Unsigned 16-bit integer (1 register)</summary>
        UInt16,

        /// <summary>Signed 32-bit integer (2 registers)</summary>
        Int32,

        /// <summary>Unsigned 32-bit integer (2 registers)</summary>
        UInt32,

        /// <summary>Signed 64-bit integer (4 registers)</summary>
        Int64,

        /// <summary>Unsigned 64-bit integer (4 registers)</summary>
        UInt64,

        /// <summary>IEEE 754 32-bit floating point (2 registers)</summary>
        Float,

        /// <summary>IEEE 754 64-bit floating point (4 registers)</summary>
        Double
    }
}
