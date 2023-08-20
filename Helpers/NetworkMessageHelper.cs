using System;
using PBUdpTransport.Utils;

namespace PBUdpTransport.Helpers
{
    internal static class NetworkMessageHelper
    {
        public static EPacketFlags GetPacketFlags(byte[] data)
        {
            var byteSpan = new Span<byte>(data);
            var flagsSpan = byteSpan.Slice(2, 2);
            var flagsInt = BitConverter.ToUInt16(flagsSpan);
            if(!Enum.TryParse(flagsInt.ToString(), out EPacketFlags flags))
            {
                Console.WriteLine("inavlid enum type");
            }
            
            return flags;
        }
        
        public static EProtocolType GetProtocolType(byte[] data)
        {
            var byteSpan = new Span<byte>(data);
            var flagsSpan = byteSpan.Slice(0, 2);
            
            return (EProtocolType)BitConverter.ToUInt16(flagsSpan);
        }

        public static int GetMessageLength(byte[] data)
        {
            var byteSpan = new Span<byte>(data);
            var lengthSpan = byteSpan.Slice(8, 4);

            return BitConverter.ToInt32(lengthSpan);
        }
        
        public static ushort GetPacketId(byte[] data)
        {
            var byteSpan = new Span<byte>(data);
            var lengthSpan = byteSpan.Slice(6, 2);

            return BitConverter.ToUInt16(lengthSpan);
        }
        
        public static ushort GetTransmissionId(byte[] data)
        {
            var byteSpan = new Span<byte>(data);
            var lengthSpan = byteSpan.Slice(4, 2);

            return BitConverter.ToUInt16(lengthSpan);
        }
        
        public static ushort GetWindowSize(byte[] data)
        {
            //TODO:
            var byteSpan = new Span<byte>(data);
            var lengthSpan = byteSpan.Slice(12, 2);

            return BitConverter.ToUInt16(lengthSpan);
        }
        
        public static ESendMode GetSendMode(byte[] data)
        {
            //TODO:
            var byteSpan = new Span<byte>(data);
            var sendModeSpan = byteSpan.Slice(14, 2);

            return (ESendMode)BitConverter.ToUInt16(sendModeSpan);
        }
        
        public static int GetMessageSize(byte[] data)
        {
            //TODO:
            var byteSpan = new Span<byte>(data);
            var lengthSpan = byteSpan.Slice(12, 4);

            return BitConverter.ToUInt16(lengthSpan);
        }
        public static ArraySegment<byte> GetBytes(byte[] data, int startPos, int count)
        {
            var arraySegment = new ArraySegment<byte>(data, startPos, count);

            return arraySegment;
        }
        
        public static void AddBytes(byte[] target, byte[] source, int startPos, int count)
        {
            for (int i = 0; i < count; i++)
            {
                target[startPos + i] = source[i];
            }
        }
    }
}