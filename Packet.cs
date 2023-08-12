using System;

namespace UdpTransport
{
    public class Packet
    {
        public byte[] Payload;
        public ushort PacketId;
        public DateTime ResendTime;
        public int ResendAttemptCount;
        public bool HasAck;
        public int Count;
    }
}