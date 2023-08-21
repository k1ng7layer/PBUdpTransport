using System;
using System.Collections.Concurrent;
using System.Net;
using PBUdpTransport.Utils;

namespace PBUdpTransport.Models
{
    internal class UdpTransmission
    {
        public ushort Id { get; set; }
        public IPEndPoint RemoteEndPoint { get; set; }
        public ConcurrentDictionary<ushort, Packet> Packets { get; set; }
        public ushort WindowLowerBoundIndex { get; set; }
        public ushort WindowSize { get; set; }
        public ushort SmallestPendingPacketIndex { get; set; }
        public EventHandler<CompletedTransmissionArgs> Completed { get; set; }
        public int ReceivedLenght { get; set; }
        public ESendMode SendMode { get; set; }
        public ushort LasPacketId { get; set; }
        public DateTime LastDatagramReceiveTime { get; set; }
        public bool IsCompleted { get; set; }
    }
}