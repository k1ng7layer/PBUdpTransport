using System;
using System.Collections.Concurrent;
using PBUdpTransport.Models;
using PBUdpTransport.Utils;

namespace PBUdpTransport.Helpers
{
    internal static class PacketHelper
    {
        public static ConcurrentDictionary<ushort, Packet> CreatePacketSequence(byte[] data, 
            int mtu, 
            ushort sequenceId,
            ushort windowSize,
            ESendMode sendMode)
        {
            const int headersLength = 8;

            var firstPacket = CreateControlPacket(EPacketFlags.FirstPacket, data.Length, sequenceId, 0, windowSize, sendMode);

            ushort packetId = 1;
            var span = new Span<byte>(data);

            var byteWriter = new ByteWriter(6);
            
            byteWriter.AddUshort((ushort)EProtocolType.RUDP);
            byteWriter.AddUshort((ushort)EPacketFlags.Default);
            byteWriter.AddUshort(sequenceId);

            var dictionary = new ConcurrentDictionary<ushort, Packet>();
            dictionary.TryAdd(firstPacket.PacketId, firstPacket);
            // multiply by headers count including packet ID bytes size
            var writeOffset = sizeof(ushort) * 4; 
            
            var packetHeaders = byteWriter.Data;
            
            var remainingLength = data.Length;

            for (var i = 0; i < (data.Length); i += (mtu - headersLength))
            {
                var lengthToRead = remainingLength <= (mtu - headersLength) ? remainingLength : (mtu - headersLength);
                var totalPayload = new byte[lengthToRead + headersLength];
                var packetIdBytes = BitConverter.GetBytes(packetId);
                try
                {
                    var clientPayload = span.Slice(i, lengthToRead).ToArray();
                
                    Buffer.BlockCopy(packetHeaders, 0, totalPayload, 0, packetHeaders.Length);
                    Buffer.BlockCopy(packetIdBytes, 0, totalPayload, sizeof(ushort) * 3, packetIdBytes.Length);
                    Buffer.BlockCopy(clientPayload, 0, totalPayload, writeOffset, clientPayload.Length);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"startPos = {i}, lengthToRead {lengthToRead}, remaining data length {remainingLength}, total length = {data.Length}");
                    throw;
                }
                
                var packet = new Packet
                {
                    Payload = totalPayload,
                    PacketId = packetId,
                    HasAck = sendMode == ESendMode.Unreliable
                };
                
                dictionary.TryAdd(packetId, packet);
                
                packetId++;
                
                remainingLength -= lengthToRead;
            }
            
            return dictionary;
        }

        public static int GetPacketSequenceSize(byte[] data, int mtu)
        {
            var packetsNum = data.Length / (double)mtu;
            var packetsNumRounded = (int)Math.Ceiling(packetsNum);

            return packetsNumRounded;
        }
        
        public static int GetPacketSequenceSize(int messageLength, int mtu)
        {
            var packetsNumWoHeaders = messageLength / (double)mtu;
            var packetsNumRounded = (int)Math.Ceiling(packetsNumWoHeaders);

            var totalPacketsHeadersLength = packetsNumRounded * 8;
            
            var packetNumWithHeaders = (messageLength + totalPacketsHeadersLength) / (double)mtu;
            var packetNumRoundedWithHeaders = (int)Math.Ceiling(packetNumWithHeaders);
            
            return packetNumRoundedWithHeaders + 1;
        }

        public static ushort GenerateTransmissionId()
        {
            //TODO:
            return 0;
        }
        
        public static Packet CreateControlPacket(
            EPacketFlags packetFlags, 
            int messageLength,
            ushort transmissionId, 
            ushort packetId, 
            ushort windowSize, 
            ESendMode sendMode)
        {
            var byteWriter = new ByteWriter(16);  
            byteWriter.AddUshort((ushort)EProtocolType.UDP);
            byteWriter.AddUshort((ushort)packetFlags);
            byteWriter.AddUshort(transmissionId);
            byteWriter.AddUshort(packetId);
            byteWriter.AddInt(messageLength);
            byteWriter.AddUshort(windowSize);
            byteWriter.AddUshort((ushort)sendMode);

            var packet = new Packet()
            {
                Payload = byteWriter.Data,
                PacketId = packetId,
            };

            return packet;
        }
    }
}