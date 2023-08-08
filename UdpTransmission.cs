using System;
using System.Net;

namespace UdpTransport
{
    public class UdpTransmission
    {
        public ushort Id { get; set; }
        public IPEndPoint RemoteEndPoint { get; set; }
        public Packet[] Packets { get; set; }
        public ushort WindowLowerBoundIndex { get; set; }
        public ushort WindowSize { get; set; }
        public ushort SmallestPendingPacketIndex { get; set; }
        public Action Completed { get; set; }
        public int ReceivedLenght { get; set; }
        public bool Reliable { get; set; }
    }
}