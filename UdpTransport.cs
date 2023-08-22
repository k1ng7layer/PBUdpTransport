using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PBUdpTransport.Config;
using PBUdpTransport.Config.Impl;
using PBUdpTransport.Helpers;
using PBUdpTransport.Models;
using PBUdpTransport.Utils;

namespace PBUdpTransport
{
    public class UdpTransport : IDisposable
    {
        private const int READ_TIME = 10;
        private const int MAX_SEND_TIME = 50;
        private const int SEND_DELAY = 5;
        private const int PACKET_RESENT_TIME = 100;
        private const int PACKET_MAX_SEND_TIME = 300;
        private const int UDP_HEADERS_LENGTH = 8;
        private const double TRANSMISSION_TIMEOUT = 800;
        
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly IUdpConfiguration _udpConfiguration;
        private readonly Socket _socketReceiver;
        private readonly EndPoint _localEndPoint;
        private readonly ConcurrentDictionary<IPEndPoint, ConcurrentDictionary<ushort, UdpTransmission>> _udpSenderTransmissionsTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ConcurrentDictionary<ushort, UdpTransmission>> _udpReceiverTransmissionsTable = new();
        private readonly ConcurrentQueue<RawPacket> _receivedRawPacketsQueue = new();
        private readonly ConcurrentQueue<RawPacket> _sendRawPacketsQueue = new();
        
        private EventHandler<CompletedTransmissionArgs> _receiveEventHandler;

        private ushort _transmissionsCount;
        private bool _running;

        public UdpTransport(
            EndPoint localEndPoint,
            IUdpConfiguration udpConfiguration)
        {
            _socketReceiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            
            _localEndPoint = localEndPoint;
            _udpConfiguration = udpConfiguration;
        }
        
        public UdpTransport(
            EndPoint localEndPoint)
        {
            _socketReceiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            
            _localEndPoint = localEndPoint;
            _udpConfiguration = new DefaultUdpConfiguration();
        }

        public void Start()
        {
            _running = true;
            _socketReceiver.Bind(_localEndPoint);
            
            Task.Run(async () => await ProcessSocketRawReceive(), _cancellationTokenSource.Token);
            Task.Run(async () => await ProcessSocketRawSend(), _cancellationTokenSource.Token);
            Task.Run(async () => await ProcessTransmissionsReceiveQueue(), _cancellationTokenSource.Token);
            Task.Run(async () => await ProcessTransmissionsSend(), _cancellationTokenSource.Token);
        }

        public void Stop()
        {
            _running = false;
            
            _udpReceiverTransmissionsTable.Clear();
            _udpSenderTransmissionsTable.Clear();
            _receivedRawPacketsQueue.Clear();
            _sendRawPacketsQueue.Clear();
            _cancellationTokenSource.Cancel();
            _socketReceiver.Close();
        }
        
        public Task SendAsync(byte[] data, IPEndPoint remoteEndpoint, ESendMode sendMode)
        {
            if (!_running)
                throw new Exception($"[{nameof(UdpTransport)}] you must first call the start method");
            
            var sequenceId = ++_transmissionsCount;
            
            var packets = PacketHelper.CreatePacketSequence(
                data, 
                _udpConfiguration.MTU, sequenceId, 
                _udpConfiguration.TransmissionWindowSize, sendMode);
            
            var transmission = new UdpTransmission
            {
                Packets = packets,
                WindowSize = _udpConfiguration.TransmissionWindowSize,
                SmallestPendingPacketIndex = 0,
                RemoteEndPoint = remoteEndpoint,
                SendMode = sendMode,
                Id = sequenceId,
                LasPacketId = (ushort)(packets.Count - 1),
                LastDatagramReceiveTime = DateTime.Now
            };
            
            var taskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CompletedTransmissionArgs> callback = null;
            
            callback = (_, args) =>
            {
                transmission.Completed -= callback;
                taskSource.SetResult(args.IsSuccessfullyCompleted);
            };

            transmission.Completed += callback;

            if (!_udpSenderTransmissionsTable.TryGetValue(remoteEndpoint, out var transmissionTable))
            {
                transmissionTable = new ConcurrentDictionary<ushort, UdpTransmission>();
                _udpSenderTransmissionsTable.TryAdd(remoteEndpoint, transmissionTable);
            }
            
            transmissionTable.TryAdd(sequenceId, transmission);

            return taskSource.Task;
        }

        public void Send(byte[] data, IPEndPoint remoteEndpoint, ESendMode sendMode)
        {
            var sequenceId = ++_transmissionsCount;
            
            var packets = PacketHelper.CreatePacketSequence(
                data, 
                _udpConfiguration.MTU, 
                sequenceId, 
                _udpConfiguration.TransmissionWindowSize, sendMode);
            
            var transmission = new UdpTransmission
            {
                Packets = packets,
                WindowSize = _udpConfiguration.TransmissionWindowSize,
                SmallestPendingPacketIndex = 0,
                RemoteEndPoint = remoteEndpoint,
                SendMode = sendMode,
                Id = sequenceId,
                LasPacketId = (ushort)(packets.Count - 1),
                LastDatagramReceiveTime = DateTime.Now
            };
            
            if (!_udpSenderTransmissionsTable.TryGetValue(remoteEndpoint, out var transmissionTable))
            {
                transmissionTable = new ConcurrentDictionary<ushort, UdpTransmission>();
                _udpSenderTransmissionsTable.TryAdd(remoteEndpoint, transmissionTable);
            }
            
            transmissionTable.TryAdd(sequenceId, transmission);
        }

        public Task<TransportMessage> ReceiveAsync()
        {
            if (!_running)
                throw new Exception($"[{nameof(UdpTransport)}] you must first call the start method");
            
            var taskSource = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            _receiveEventHandler = (_, args) =>
            {
                if (args.IsSuccessfullyCompleted)
                {
                    taskSource.SetResult(args.TransportMessage);
                }
                else
                {
                    taskSource.SetCanceled();
                }
            };

            return taskSource.Task;
        }
        
        private void CreateTransmission(byte[] data, IPEndPoint remoteEndPoint)
        {
            
            var messageLength = NetworkMessageHelper.GetMessageLength(data);
            var id = NetworkMessageHelper.GetTransmissionId(data);
            var windowSize = NetworkMessageHelper.GetWindowSize(data);
            var transmissionId = NetworkMessageHelper.GetTransmissionId(data);
            var sendMode = NetworkMessageHelper.GetSendMode(data);
            
            var packetSequenceLength = PacketHelper.GetPacketSequenceSize(messageLength, _udpConfiguration.MTU);
            var hasTransmissionsTable =
                _udpReceiverTransmissionsTable.TryGetValue(remoteEndPoint, out var transmissions);
            
            SendAck(transmissionId, remoteEndPoint, 0);
            
            if(hasTransmissionsTable && transmissions.ContainsKey(transmissionId))
                return;

            var packets = new ConcurrentDictionary<ushort, Packet>();

            for (ushort i = 0; i < packetSequenceLength; i++)
            {
                var packet = new Packet
                {
                    PacketId = i,
                    HasAck = sendMode == ESendMode.Unreliable || i == 0,
                    ResendAttemptCount = _udpConfiguration.MaxPacketResendCount,
                    ResendTime = DateTime.Now
                };
                
                packets.TryAdd(i, packet);
            }
            
            var transmission = new UdpTransmission()
            {
                Id = id,
                WindowSize = windowSize,
                WindowLowerBoundIndex = 0,
                SmallestPendingPacketIndex = 0,
                Packets = packets,
                RemoteEndPoint = remoteEndPoint,
                LasPacketId = (ushort)(packetSequenceLength - 1),
                LastDatagramReceiveTime = DateTime.Now,
                SendMode = sendMode
            };

            ConcurrentDictionary<ushort, UdpTransmission> clientTransmissionTable;
            if (_udpReceiverTransmissionsTable.ContainsKey(remoteEndPoint))
            {
                clientTransmissionTable = _udpReceiverTransmissionsTable[remoteEndPoint];
            }
            else
            {
                clientTransmissionTable = new ConcurrentDictionary<ushort, UdpTransmission>();
                _udpReceiverTransmissionsTable.TryAdd(remoteEndPoint, clientTransmissionTable);
            }
            
            clientTransmissionTable.TryAdd(transmissionId, transmission);
            
            ShiftTransmissionWindow(transmission);
        }
        
        private bool TryGetSenderTransmission(ushort transmissionId, IPEndPoint endPoint, out UdpTransmission transmission)
        {
            var hasTransmissionTable = _udpSenderTransmissionsTable.TryGetValue(endPoint, out var transmissions);

            if (!hasTransmissionTable)
            {
                transmission = null;
                return false;
            }
            
            var hasTransmission = transmissions.TryGetValue(transmissionId, out transmission);

            if (!hasTransmission)
                return false;

            return true;
        }
        
       private async Task ProcessTransmissionsSend()
        {
            while (_running)
            {
                var sendTransmissionsTables = _udpSenderTransmissionsTable;

                foreach (var sendTransmissionsTable in sendTransmissionsTables.Values)
                {
                    foreach (var transmission in sendTransmissionsTable.Values)
                    {
                        try
                        {
                            if ((DateTime.Now - transmission.LastDatagramReceiveTime).TotalMilliseconds >
                                TRANSMISSION_TIMEOUT)
                            {
                                TryStopSenderTransmission(transmission);
                                TryStopReceiverTransmission(transmission);
                                
                                throw new SocketException(10060);
                            }

                            if (transmission.SendMode == ESendMode.Reliable)
                            {
                                await ReliableSendCycle(transmission);
                            }
                            else
                            {
                                await UnReliableSend(transmission);
                                sendTransmissionsTable.TryRemove(transmission.Id, out _);
                                transmission.Completed?.Invoke(this, new CompletedTransmissionArgs(null, true));
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
            }
        }


       private async Task UnReliableSend(UdpTransmission transmission)
       {
           var maxSendTime = DateTime.Now.AddMilliseconds(PACKET_MAX_SEND_TIME);

           var windowUpperBound = transmission.Packets.Count;
           
           for (var i = transmission.WindowLowerBoundIndex;
                i < windowUpperBound && DateTime.Now < maxSendTime && i <= transmission.Packets.Count - 1 && transmission.Packets.Count > 0; 
                i++)
           {
               try
               {
                   transmission.Packets.TryGetValue(i, out var packet);
                   //await Console.Out.WriteLineAsync($"send packet with id {packet.PacketId}");
                   if (packet != null && packet.ResendTime <= DateTime.Now)
                   {
                       packet.ResendTime = DateTime.Now.AddMilliseconds(PACKET_RESENT_TIME);
                
                       packet.ResendAttemptCount++;
                                        
                       await _socketReceiver.SendToAsync(packet.Payload, SocketFlags.None, transmission.RemoteEndPoint);
                                    
                       await Task.Delay(SEND_DELAY);
                   }
               }
               catch (Exception e)
               {
                   transmission.Completed?.Invoke(this, new CompletedTransmissionArgs(null, false));
                   Console.WriteLine(e);
                   throw;
               }
           }
       }

       private async Task ReliableSendCycle(UdpTransmission transmission)
       {
           var maxSendTime = DateTime.Now.AddMilliseconds(PACKET_MAX_SEND_TIME);

           var windowUpperBound = transmission.SendMode == ESendMode.Reliable
               ? transmission.WindowLowerBoundIndex + transmission.WindowSize
               : transmission.Packets.Count;
           
           for (var i = transmission.WindowLowerBoundIndex;
                i < windowUpperBound && DateTime.Now < maxSendTime && i <= transmission.Packets.Count - 1 && transmission.Packets.Count > 0; 
                i++)
           {
               try
               {
                   transmission.Packets.TryGetValue(i, out var packet);
                   //await Console.Out.WriteLineAsync($"send packet with id {packet.PacketId}");
                   if (packet != null && packet.ResendTime <= DateTime.Now && !packet.HasAck && packet.ResendAttemptCount < _udpConfiguration.MaxPacketResendCount)
                   {
                       packet.ResendTime = DateTime.Now.AddMilliseconds(PACKET_RESENT_TIME);
                
                       packet.ResendAttemptCount++;
                                        
                       await _socketReceiver.SendToAsync(packet.Payload, SocketFlags.None, transmission.RemoteEndPoint);
                                    
                       await Task.Delay(SEND_DELAY);
                   }
               }
               catch (Exception e)
               {
                   transmission.Completed?.Invoke(this, new CompletedTransmissionArgs(null, false));
                   Console.WriteLine(e);
                   throw;
               }
           }
       }

       private async Task ProcessSocketRawReceive()
        {
            try
            {
                while (_running)
                {
                    try
                    {
                        var iEndpoint = new IPEndPoint(IPAddress.Any, 0);
                    
                        var data = new byte[_udpConfiguration.ReceiveBufferSize];
                        
                        var receiveFromResult = await _socketReceiver.ReceiveFromAsync(data, SocketFlags.None, iEndpoint);
                    
                        var rawPacket = new RawPacket((IPEndPoint)receiveFromResult.RemoteEndPoint, data, receiveFromResult.ReceivedBytes);
                        _receivedRawPacketsQueue.Enqueue(rawPacket);
                    }
                    catch (Exception e)
                    {
                        //Console.WriteLine(e);
                    }
                }
            }
            catch (Exception e)
            {
                _receiveEventHandler?.Invoke(this, new CompletedTransmissionArgs(null, false));
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task ProcessSocketRawSend()
        {
            while (_running)
            {
                var maxSendTime = DateTime.Now.AddMilliseconds(MAX_SEND_TIME);

                while (_sendRawPacketsQueue.Count > 0 && maxSendTime > DateTime.Now)
                {
                    _sendRawPacketsQueue.TryDequeue(out var packet);

                    await _socketReceiver.SendToAsync(packet.Payload, SocketFlags.None, packet.EndPoint);
                }
            }
        }

        private async Task ProcessTransmissionsReceiveQueue()
        {
            try
            {
                while (_running)
                {
                    var startRead = DateTime.Now;
            
                    var readTime = startRead.AddMilliseconds(READ_TIME);

                    try
                    {
                        while (_receivedRawPacketsQueue.Count > 0 && readTime > DateTime.Now)
                        {
                            _receivedRawPacketsQueue.TryDequeue(out var packet);

                            HandleRawPacket(packet);
                        }

                        await Task.Delay(READ_TIME);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private bool TryStopReceiverTransmission(UdpTransmission transmission)
        {
            if(_udpReceiverTransmissionsTable.TryGetValue(transmission.RemoteEndPoint, out var transmissions))
            {
                transmissions.TryRemove(transmission.Id, out _);
                _receiveEventHandler(this, new CompletedTransmissionArgs(null, false));

                return true;
            }

            return false;
        }
        
        private bool TryStopSenderTransmission(UdpTransmission transmission)
        {
            if(_udpSenderTransmissionsTable.TryGetValue(transmission.RemoteEndPoint, out var transmissions))
            {
                transmissions.TryRemove(transmission.Id, out _);
                
                transmission.Completed(this, new CompletedTransmissionArgs(null, false));

                return true;
            }

            return false;
        }

        private void HandleRawPacket(RawPacket rawPacket)
        {
            var data = rawPacket.Payload;

            var ipEndpoint = rawPacket.EndPoint;
            
            var packetFlags = NetworkMessageHelper.GetPacketFlags(data);
            var packetId = NetworkMessageHelper.GetPacketId(data);
            var transmissionId = NetworkMessageHelper.GetTransmissionId(data);
            
            UdpTransmission transmission;
            
            var hasTransmission = packetFlags == EPacketFlags.Ack ? 
                TryGetSenderTransmission(transmissionId, ipEndpoint, out transmission) : 
                TryGetReceiverTransmission(transmissionId, ipEndpoint, out transmission);

            switch (packetFlags)
            {
                case EPacketFlags.Ack:
                    
                    if(!hasTransmission)
                        break;
                    
                    HandleAck(transmission, packetId);
                    break;
                case EPacketFlags.Default:
                    
                    if(!hasTransmission)
                        break;
                    if(transmission.SendMode == ESendMode.Reliable)
                        SendAck(transmission.Id, ipEndpoint, packetId);
                    WritePacket(transmission, data, packetId, rawPacket.Count);
                    break;
                case EPacketFlags.FirstPacket:
                    CreateTransmission(data, ipEndpoint);
                    break;
            }
        }

        private bool TryGetReceiverTransmission(ushort transmissionId, IPEndPoint endPoint, out UdpTransmission transmission)
        {
            var hasTransmissionTable = _udpReceiverTransmissionsTable.TryGetValue(endPoint, out var transmissions);

            if (!hasTransmissionTable)
            {
                transmission = null;
                return false;
            }
            
            var hasTransmission = transmissions.TryGetValue(transmissionId, out transmission);

            if (!hasTransmission)
                return false;

            return true;
        }
        
        private void WritePacket(UdpTransmission transmission, byte[] data, ushort packetId, int count)
        {
            if (transmission.Packets.TryGetValue(packetId, out var packet))
            {
                if(packet.HasAck && transmission.SendMode == ESendMode.Reliable)
                    return;
                
                packet.Payload = data;
                packet.Count = count;
                packet.HasAck = true;
                
                transmission.ReceivedLenght += count;
                transmission.LastDatagramReceiveTime = DateTime.Now;
            }

            if (transmission.SendMode == ESendMode.Unreliable)
            {
                transmission.SmallestPendingPacketIndex = packetId;
            }

            if (packetId == transmission.SmallestPendingPacketIndex && transmission.SendMode == ESendMode.Reliable)
            {
                ShiftTransmissionWindow(transmission);
            }
            
            if (transmission.SmallestPendingPacketIndex == transmission.LasPacketId)
            {
                PrepareMessage(transmission);
            }
        }

        private void PrepareMessage(UdpTransmission transmission)
        {
            var hasTransmissions =  _udpReceiverTransmissionsTable.TryGetValue(transmission.RemoteEndPoint, out var transmissions);

            if (hasTransmissions)
            {
                transmissions.TryRemove(transmission.Id, out _);
            }

            transmission.IsCompleted = true;
            var messagePayload = new byte[transmission.ReceivedLenght - UDP_HEADERS_LENGTH * (transmission.Packets.Count - 1)];
            var offset = 0;
            foreach (var packet in transmission.Packets.Values)
            {
                if(packet.PacketId == 0)
                    continue;

                Buffer.BlockCopy(packet.Payload, 
                    UDP_HEADERS_LENGTH,
                    messagePayload,
                    offset, 
                    packet.Count - UDP_HEADERS_LENGTH);
                
                offset += packet.Count - UDP_HEADERS_LENGTH;
            }
            
            var message = new TransportMessage(messagePayload, transmission.RemoteEndPoint);

            _receiveEventHandler?.Invoke(this, new CompletedTransmissionArgs(message, true));
            //_transportMessagesQueue.Enqueue(message);
        }
        
        private void HandleAck(UdpTransmission transmission, ushort packetId)
        {
            if(!TryGetSenderTransmission(transmission.Id, transmission.RemoteEndPoint, out _))
                return;

            var windowUpperBound = transmission.WindowLowerBoundIndex + transmission.WindowSize;
            
            //packet doesnt belongs to current window
            if (packetId < transmission.WindowLowerBoundIndex || packetId > windowUpperBound - 1)
            {
                Console.WriteLine($"income packet with id {packetId} is out of window range");
                return;
            }
            
            var packet = transmission.Packets[packetId];
            
            packet.HasAck = true;
            transmission.LastDatagramReceiveTime = DateTime.Now;
            
            if (transmission.SmallestPendingPacketIndex == transmission.LasPacketId)
            {
                var transmissionsTable = _udpSenderTransmissionsTable[transmission.RemoteEndPoint];
                transmissionsTable.Remove(transmission.Id, out _);
                transmission.Completed?.Invoke(this, new CompletedTransmissionArgs(null, true));
                
                return;
            }
            
            if (packetId == transmission.SmallestPendingPacketIndex)
            {
                ShiftTransmissionWindow(transmission);
            }
        }

        private void SendAck(ushort transmissionId, IPEndPoint remoteEndpoint, ushort packetId)
        {
            var byteWriter = new ByteWriter();
            byteWriter.AddUshort((ushort)EProtocolType.UDP);
            byteWriter.AddUshort((ushort)EPacketFlags.Ack);
            byteWriter.AddUshort(transmissionId);
            byteWriter.AddUshort(packetId);
            
            var rawPacket = new RawPacket(remoteEndpoint, byteWriter.Data, byteWriter.WritePos);
       
            _sendRawPacketsQueue.Enqueue(rawPacket);
        }
        
        private void ShiftTransmissionWindow(UdpTransmission transmission)
        {
            var windowUpperBound = transmission.WindowLowerBoundIndex + transmission.WindowSize;
            var lastPacketIndex = transmission.LasPacketId;

            for (var i = (ushort)(transmission.SmallestPendingPacketIndex + 1);
                 i < windowUpperBound + 1 && i <= lastPacketIndex; 
                 i++)
            {
                var packet = transmission.Packets[i];

                if (!packet.HasAck)
                {
                    transmission.SmallestPendingPacketIndex = i;
                    transmission.WindowLowerBoundIndex = i;
                    break;
                }
                
                transmission.SmallestPendingPacketIndex = i;
                transmission.WindowLowerBoundIndex++;
            }
        }

        public void Dispose()
        {
            _socketReceiver?.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }
}