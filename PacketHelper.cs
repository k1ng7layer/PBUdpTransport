using System;
using System.Collections.Concurrent;
using UdpTransport;

namespace PBUdpTransport
{
    internal static class PacketHelper
    {
        public static ConcurrentDictionary<ushort, Packet> CreatePacketSequence(byte[] data, 
            int mtu, 
            ushort sequenceId)
        {
             var packetsNum = data.Length / (double)mtu;
            var packetsNumRounded = (int)Math.Round(packetsNum, MidpointRounding.ToPositiveInfinity);
            var totalPackets = packetsNumRounded + 1;
            var packets = new Packet[totalPackets];

            var firstPacket = CreateControlPacket(EPacketFlags.FirstPacket, data.Length, sequenceId, 0);
            //var lastPacket = CreateControlPacket(EPacketFlags.LastPacket, data.Length, sequenceId, (ushort)(totalPackets - 1));
            
            packets[0] = firstPacket;
            //packets[totalPackets - 1] = lastPacket;
            
            ushort packetId = 1;
            var span = new Span<byte>(data);

            var byteWriter = new ByteWriter(6);
            
            byteWriter.AddUshort((ushort)EProtocolType.RUDP);
            byteWriter.AddUshort((ushort)EPacketFlags.Default);
            byteWriter.AddUshort(sequenceId);

            var dictionary = new ConcurrentDictionary<ushort, Packet>();
            dictionary.TryAdd(firstPacket.PacketId, firstPacket);
            //dictionary.TryAdd(lastPacket.PacketId, lastPacket);
            // multiply by headers count including packet ID bytes size
            var writeOffset = sizeof(ushort) * 4; 
            
            var packetHeaders = byteWriter.Data;
            
            var remainingLength = data.Length;
            
            for (var i = 0; i < data.Length; i++)
            {
                if (i % (data.Length / packetsNumRounded) == 0)
                {
                    var lengthToRead = remainingLength < mtu ? remainingLength : mtu;
               
                    var clientPayload = span.Slice(i, lengthToRead).ToArray();
            
                    var totalPayload = new byte[packetHeaders.Length + sizeof(ushort) + clientPayload.Length];
                    
                    var packetIdBytes = BitConverter.GetBytes(packetId);
                    
                    Buffer.BlockCopy(packetHeaders, 0, totalPayload, 0, packetHeaders.Length);
                    Buffer.BlockCopy(packetIdBytes, 0, totalPayload, sizeof(ushort) * 3, packetIdBytes.Length);
                    Buffer.BlockCopy(clientPayload, 0, totalPayload, writeOffset, clientPayload.Length);
                    
                    var packet = new Packet
                    {
                        Payload = totalPayload,
                        PacketId = packetId
                    };

                    dictionary.TryAdd(packetId, packet);
                    packets[packetId++] = packet;
                    
                    remainingLength -= lengthToRead;
                }
            }

            return dictionary;
        }

        public static int GetPacketSequenceSize(byte[] data, int mtu)
        {
            var packetsNum = data.Length / (double)mtu;
            var packetsNumRounded = (int)Math.Round(packetsNum, MidpointRounding.AwayFromZero);

            return packetsNumRounded;
        }
        
        public static int GetPacketSequenceSize(int messageLength, int mtu)
        {
            var packetsNum = messageLength / (double)mtu;
            var packetsNumRounded = (int)Math.Round(packetsNum, MidpointRounding.ToPositiveInfinity);

            return packetsNumRounded;
        }

        public static ushort GenerateTransmissionId()
        {
            //TODO:
            return 0;
        }
        
        public static Packet CreateControlPacket(
            EPacketFlags packetFlags, 
            int messageLength,
            ushort transmissionId, ushort packetId)
        {
            var byteWriter = new ByteWriter(12);  
            byteWriter.AddUshort((ushort)EProtocolType.UDP);
            byteWriter.AddUshort((ushort)packetFlags);
            byteWriter.AddUshort(transmissionId);
            byteWriter.AddUshort(packetId);
            byteWriter.AddInt(messageLength);

            var packet = new Packet()
            {
                Payload = byteWriter.Data,
                PacketId = packetId,
            };

            return packet;
        }
    }
}