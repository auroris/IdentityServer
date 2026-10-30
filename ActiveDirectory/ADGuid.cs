using System;

namespace ActiveDirectory
{
    /// <summary>
    /// Represents an Active Directory object GUID as a raw byte sequence,
    /// with conversions to and from hexadecimal strings.
    /// <para>
    /// The built-in <see cref="System.Guid"/> class reorders bytes to match the Windows
    /// mixed-endian GUID layout, which produces a different hex string than the one AD
    /// stores in <c>objectGUID</c> and uses in <c>LDAP://&lt;GUID=...&gt;</c> paths.
    /// This class stores bytes as-is, preserving the AD wire-format order.
    /// </para>
    /// </summary>
    public class ADGuid
    {
        /// <summary>Initialises an empty <see cref="ADGuid"/> with no byte data.</summary>
        public ADGuid()
        {
            Bytes = null;
        }

        /// <summary>
        /// Initialises an <see cref="ADGuid"/> from a raw byte array,
        /// such as the value returned by <c>result.Properties["objectguid"][0]</c>.
        /// </summary>
        /// <param name="bytes">The raw GUID bytes in AD wire-format order.</param>
        public ADGuid(byte[] bytes)
        {
            this.Bytes = bytes;
        }

        /// <summary>
        /// Initialises an <see cref="ADGuid"/> from a hexadecimal string,
        /// such as the value returned by <see cref="Convert(byte[])"/> or <see cref="ToString"/>.
        /// </summary>
        /// <param name="hex">A 32-character lowercase hex string (no hyphens or braces).</param>
        public ADGuid(string hex)
        {
            Bytes = HexToByte(hex);
        }

        private byte[]? bytes;

        /// <summary>
        /// Gets or sets the raw GUID bytes in AD wire-format order.
        /// Setting this property also updates the value returned by <see cref="Guid"/> and <see cref="ToString"/>.
        /// </summary>
        public byte[]? Bytes
        {
            get
            {
                return bytes;
            }

            set
            {
                bytes = value;
            }
        }

        /// <summary>
        /// Gets or sets the GUID as a 32-character lowercase hexadecimal string (no hyphens or braces).
        /// This is the format expected by <c>LDAP://&lt;GUID=...&gt;</c> bind strings.
        /// Setting this property converts the hex string back to bytes.
        /// </summary>
        public string Guid
        {
            get
            {
                return ByteToHex(bytes);
            }
            set
            {
                bytes = HexToByte(value);
            }
        }

        /// <summary>
        /// Returns the GUID as a 32-character lowercase hexadecimal string,
        /// equivalent to <see cref="Guid"/>.
        /// </summary>
        public override string ToString()
        {
            return Guid;
        }

        /// <summary>
        /// Converts a raw GUID byte array to a 32-character lowercase hexadecimal string.
        /// </summary>
        /// <param name="guid">The raw GUID bytes in AD wire-format order.</param>
        /// <returns>A 32-character lowercase hex string.</returns>
        public static string Convert(byte[] guid)
        {
            return ByteToHex(guid);
        }

        /// <summary>
        /// Converts a 32-character lowercase hexadecimal GUID string back to a raw byte array.
        /// </summary>
        /// <param name="guid">A 32-character lowercase hex string (no hyphens or braces).</param>
        /// <returns>The raw GUID bytes in AD wire-format order.</returns>
        public static Byte[] Convert(string guid)
        {
            return HexToByte(guid);
        }

        /// <summary>
        /// Returns the raw GUID bytes as an array, equivalent to reading <see cref="Bytes"/>.
        /// </summary>
        /// <returns>The raw GUID bytes in AD wire-format order, or <c>null</c> if not initialised.</returns>
        public byte[]? ToByteArray()
        {
            return bytes;
        }

        // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa
        static byte[] HexToByte(string input)
        {
            var outputLength = input.Length / 2;
            var output = new byte[outputLength];
            var numeral = new char[2];
            for (int i = 0; i < outputLength; i++)
            {
                input.CopyTo(i * 2, numeral, 0, 2);
                output[i] = System.Convert.ToByte(new string(numeral), 16);
            }
            return output;
        }

        static string ByteToHex(byte[]? bytes)
        {
            if (bytes == null) return "";
            char[] c = new char[bytes.Length * 2];
            int b;
            for (int i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                c[i * 2] = (char)(87 + b + (((b - 10) >> 31) & -39));
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char)(87 + b + (((b - 10) >> 31) & -39));
            }
            return new string(c);
        }
    }
}
