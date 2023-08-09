using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UdpTransport
{
    public class UdpTransport
    {
        private const int READ_TIME = 100;
        private const int MAX_SEND_TIME = 300;
        
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly IUdpConfiguration _udpConfiguration;
        private readonly Socket _socketReceiver;
        private readonly EndPoint _localEndPoint;
        private readonly ConcurrentDictionary<IPEndPoint, ConcurrentDictionary<ushort, UdpTransmission>> _udpSenderTransmissionsTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ConcurrentDictionary<ushort, UdpTransmission>> _udpReceiverTransmissionsTable = new();
        private readonly Queue<TransportMessage> _transportMessagesQueue = new();
        private readonly Queue<RawPacket> _receivedRawPacketsQueue = new();
        private readonly Queue<RawPacket> _sendRawPacketsQueue = new();
        private readonly object _locker = new();
        private readonly BlockingCollection<UdpTransmission> _udpTransmissions = new();
        private int _transmissionsCount;
        private bool _running;

        public UdpTransport(
            EndPoint localEndPoint,
            IUdpConfiguration udpConfiguration)
        {
            _socketReceiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            
            _localEndPoint = localEndPoint;
            _udpConfiguration = udpConfiguration;
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
            
            _cancellationTokenSource.Cancel();
            _socketReceiver.Close();
        }
        
        public void Update()
        {
            // if(_running)
            //     _udpListener.Receive();
        }
        
        public Task SendAsync(byte[] data, IPEndPoint remoteEndpoint, bool reliable)
        {
            var sequenceId = (ushort)++_transmissionsCount;
            
            var packets = PacketHelper.CreatePacketSequence(data, _udpConfiguration.MTU, sequenceId);
            
            var transmission = new UdpTransmission
            {
                Packets = packets,
                WindowSize = 3,
                SmallestPendingPacketIndex = 0,
                RemoteEndPoint = remoteEndpoint,
                Reliable = reliable,
                Id = sequenceId
            };
            
            
            var taskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            transmission.Completed += () =>
            {
                taskSource.SetResult(true);
            };
            
            lock (_locker)
            {
                if (!_udpSenderTransmissionsTable.TryGetValue(remoteEndpoint, out var transmissionTable))
                {
                    transmissionTable = new ConcurrentDictionary<ushort, UdpTransmission>();
                    _udpSenderTransmissionsTable.TryAdd(remoteEndpoint, transmissionTable);
                    transmissionTable.TryAdd(sequenceId, transmission);
                }
            }

            return taskSource.Task;
        }

        public void Send(byte[] data, IPEndPoint remoteEndPoint)
        {
           // _socketReceiver.Send(data, remoteEndPoint);
        }

        public Task<TransportMessage> ReceiveAsync()
        { 
            var taskSource = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            while (true)
            {
                if (_transportMessagesQueue.TryDequeue(out var message))
                {
                    taskSource.SetResult(message);
                    break;
                }
            }

            return taskSource.Task;
        }
        
        private void CreateTransmission(byte[] data, IPEndPoint remoteEndPoint, Packet incomeFirstPacket)
        {
            
            var messageLength = NetworkMessageHelper.GetMessageLength(data);
            var id = NetworkMessageHelper.GetTransmissionId(data);
            //var windowSize = NetworkMessageHelper.GetWindowSize(data);
            ushort windowSize = 3;
            var packetSequenceLength = PacketHelper.GetPacketSequenceSize(messageLength, _udpConfiguration.MTU);

            var transmissionId = NetworkMessageHelper.GetTransmissionId(data);

            SendAck(transmissionId, remoteEndPoint, incomeFirstPacket);
            
            var hasTransmissionsTable =
                _udpReceiverTransmissionsTable.TryGetValue(remoteEndPoint, out var transmissions);
            
            if(hasTransmissionsTable && transmissions.ContainsKey(transmissionId))
                return;
            
            var transmission = new UdpTransmission()
            {
                Id = id,
                WindowSize = windowSize,
                WindowLowerBoundIndex = 0,
                SmallestPendingPacketIndex = 0,
                Packets = new Packet[packetSequenceLength],
                RemoteEndPoint = remoteEndPoint,
            };

            transmission.Packets[0] = incomeFirstPacket;

            var clientTransmissionTable = new ConcurrentDictionary<ushort, UdpTransmission>();
            lock (_locker)
            {
                clientTransmissionTable.TryAdd(transmissionId, transmission);
            
                _udpReceiverTransmissionsTable.TryAdd(remoteEndPoint, clientTransmissionTable);
            }
            
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
            try
            {
                while (_running)
                {
                    var sendTransmissionsTables = _udpSenderTransmissionsTable;

                    foreach (var sendTransmissionsTable in sendTransmissionsTables.Values)
                    {
                        foreach (var transmission in sendTransmissionsTable.Values)
                        {
                            var maxSendTime = DateTime.Now.AddMilliseconds(300);

                            var windowUpperBound = transmission.WindowLowerBoundIndex + transmission.WindowSize;
            
                            for (var i = transmission.WindowLowerBoundIndex;
                                 i < windowUpperBound && DateTime.Now < maxSendTime; 
                                 i++)
                            {
                            
                                var packet = transmission.Packets[i];

                                if (packet.ResendTime <= DateTime.Now && !packet.HasAck)
                                {
                                    packet.ResendTime = DateTime.Now.AddMilliseconds(100);
                
                                    packet.ResendAttemptCount++;

                                    Console.WriteLine($"sending packet with id = {i}");
                                    // Console.WriteLine($"sending packet with id {i}, windowUpperBound = {windowUpperBound}, transmission.WindowLowerBoundIndex = {transmission.WindowLowerBoundIndex}");
                                    await _socketReceiver.SendToAsync(packet.Payload, SocketFlags.None, transmission.RemoteEndPoint);
                                    await Task.Delay(200);
                                    // Console.WriteLine($"continue sending");
                                }
                            }
                        }
                    }

                    await Task.Delay(100);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
           
        }

        private async Task ProcessSocketRawReceive()
        {
            var data = new byte[1024];
            var iEndpoint = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (_running)
                {
                    var receiveFromResult = await _socketReceiver.ReceiveFromAsync(data, SocketFlags.None, iEndpoint);
                    
                    var rawPacket = new RawPacket((IPEndPoint)receiveFromResult.RemoteEndPoint, data);
            
                    _receivedRawPacketsQueue.Enqueue(rawPacket);
                }
            }
            catch (Exception e)
            {
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
                    var packet = _sendRawPacketsQueue.Dequeue();

                    await _socketReceiver.SendToAsync(packet.Payload, SocketFlags.None, packet.EndPoint);
                }

                await Task.Delay(MAX_SEND_TIME);
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
                            // while (_receivedRawPacketsQueue.Count > 0 && readTime > DateTime.Now)
                        {
                            var rawPacket = _receivedRawPacketsQueue.Dequeue();

                            HandleRawPacket(rawPacket);
                        }

                        await Task.Delay(READ_TIME);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        continue;
                    }
             
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        
        }

        private void HandleRawPacket(RawPacket rawPacket)
        {
            var data = rawPacket.Payload;

            var ipEndpoint = rawPacket.EndPoint;

            var protocolType = NetworkMessageHelper.GetProtocolType(data);
            var packetFlags = NetworkMessageHelper.GetPacketFlags(data);

            //Console.WriteLine($"received packet with flag = {packetFlags}");
            Console.WriteLine($"received packet with flag = {packetFlags}");
            var packetId = NetworkMessageHelper.GetPacketId(data);
            var transmissionId = NetworkMessageHelper.GetTransmissionId(data);
            
            var incomePacket = new Packet()
            {
                Payload = data,
                PacketId = packetId,
                ResendAttemptCount = _udpConfiguration.MaxPacketResendCount,
                ResendTime = DateTime.Now
            };

            bool hasTransmission = false;
            UdpTransmission transmission = null;
            hasTransmission = packetFlags == EPacketFlags.Ack ? TryGetSenderTransmission(transmissionId, ipEndpoint, out transmission) :  TryGetReceiverTransmission(transmissionId, ipEndpoint, out transmission);

            switch (packetFlags)
            {
                case EPacketFlags.LastPacket:
                    
                    if(!hasTransmission)
                        break;
                    
                    SendAck(transmission.Id, ipEndpoint, incomePacket);
                    //PrepareMessage(transmission);
                    break;
                case EPacketFlags.Ack:
                    
                    if(!hasTransmission)
                        break;
                    
                    HandleAck(transmission, packetId);
                    break;
                case EPacketFlags.Default:
                    
                    if(!hasTransmission)
                        break;
                    
                    SendAck(transmission.Id, ipEndpoint, incomePacket);
                    WritePacket(transmission, data, packetId);
                    break;
                case EPacketFlags.FirstPacket:
                    
                    CreateTransmission(data, ipEndpoint, incomePacket);
                    break;
            }
        }

        private bool HasTransmissionRecords(IPEndPoint endPoint, ushort transmissionId)
        {
            if (_udpReceiverTransmissionsTable.TryGetValue(endPoint, out var udpTransmissions))
            {
                if (udpTransmissions.ContainsKey(transmissionId))
                {
                    return true;
                }
            }

            return false;
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

        private bool TryGetTransmission(ushort transmissionId, IPEndPoint endPoint, out UdpTransmission transmission)
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

        private void WritePacket(UdpTransmission transmission, byte[] data, ushort packetId)
        {
            // var packet = transmission.Packets[packetId];
            //
            // if (packet != null)
            //     return;
            
            var windowUpperBound = transmission.WindowLowerBoundIndex + transmission.WindowSize;

            if (packetId < transmission.WindowLowerBoundIndex || packetId > windowUpperBound - 1)
            {
                Console.WriteLine($"income packet with id {packetId} is out of window range");
                return;
            }
            
            var packet = new Packet
            {
                Payload = data,
                PacketId = packetId,
                ResendTime = DateTime.Now,
                HasAck = false
            };
            
            transmission.Packets[packetId - 1] = packet;
            packet.HasAck = true;

            transmission.ReceivedLenght += data.Length;
            
            var packetsLength = transmission.Packets.Length;

            if (packetId == transmission.SmallestPendingPacketIndex)
            {
                ShiftTransmissionWindow(transmission);
            }
            
            if (HasCompleteTransmission(transmission))
            // if (packetId == transmission.Packets[packetsLength - 1].PacketId)
            {
                transmission.Completed?.Invoke();
            }
          
        }

        private void PrepareMessage(UdpTransmission transmission)
        {
            var messagePayload = new byte[transmission.ReceivedLenght + sizeof(ushort) * 2];
            var offset = 0;
            
            foreach (var packet in transmission.Packets)
            {
                if(packet.PacketId == transmission.Packets.Length - 1)
                    continue;

                Buffer.BlockCopy(packet.Payload, 
                    0,
                    messagePayload,
                    offset, 
                    packet.Payload.Length);
                
                offset = packet.Payload.Length;
            }
            
            var message = new TransportMessage(messagePayload, transmission.RemoteEndPoint);

            _transportMessagesQueue.Enqueue(message);
        }
        
        private void HandleAck(UdpTransmission transmission, ushort packetId)
        {
            if(!TryGetSenderTransmission(transmission.Id, transmission.RemoteEndPoint, out var trans))
                return;
            
            Console.WriteLine($"HandleAck = {packetId}");
            
            var windowUpperBound = transmission.WindowLowerBoundIndex + transmission.WindowSize;
            
            //packet doesnt belongs to current window
            if (packetId < transmission.WindowLowerBoundIndex || packetId > windowUpperBound - 1)
            {
                Console.WriteLine($"income packet with id {packetId} is out of window range");
                return;
            }
            var packet = transmission.Packets[packetId];
            
            packet.HasAck = true;
            
            var packetsLength = transmission.Packets.Length;
            
            if (packetId == transmission.SmallestPendingPacketIndex)
            {
                ShiftTransmissionWindow(transmission);
            }
            
            // if (packetId == transmission.Packets[packetsLength - 1].PacketId)
            if (HasCompleteTransmission(transmission))
            {
                lock (_locker)
                {
                    var transmissionsTable = _udpSenderTransmissionsTable[transmission.RemoteEndPoint];
                    transmissionsTable.Remove(transmission.Id, out trans);
                }
       
                
                Console.WriteLine($"transmission.Completed");
                transmission.Completed?.Invoke();
            }
          
        }
        
        private bool HasCompleteTransmission(UdpTransmission transmission)
        {
            foreach (var packet in transmission.Packets)
            {
                if (!packet.HasAck)
                    return false;
            }

            return true;
        }

        private void SendAck(ushort transmissionId, IPEndPoint remoteEndpoint, Packet packet)
        {
            var byteWriter = new ByteWriter(8);
            byteWriter.AddUshort((ushort)EProtocolType.UDP);
            byteWriter.AddUshort((ushort)EPacketFlags.Ack);
            byteWriter.AddUshort(transmissionId);
            byteWriter.AddUshort(packet.PacketId);
            
            var rawPacket = new RawPacket(remoteEndpoint, byteWriter.Data);
            Console.WriteLine($"SendAck for packet id = {packet.PacketId}");
            _sendRawPacketsQueue.Enqueue(rawPacket);
        }
        
        private void ShiftTransmissionWindow(UdpTransmission transmission)
        {
            var smallestUnAckedPacket = transmission.SmallestPendingPacketIndex;
            
            var windowUpperBound = transmission.WindowLowerBoundIndex + transmission.WindowSize;
            var lastPacketIndex = transmission.Packets.Length - 1;
            var lastPacketId = transmission.Packets[lastPacketIndex].PacketId;
            
            // for (var i = (ushort)(smallestUnAckedPacket + 1);
            //      i < windowUpperBound + 1 && i <= lastPacketId; 
            //      i++)
            // {
            //     var packet = transmission.Packets[i];
            //
            //     if (!packet.HasAck)
            //     {
            //         transmission.SmallestPendingPacketIndex = packet.PacketId;
            //         transmission.WindowLowerBoundIndex = i;
            //         break;
            //     }
            //     
            //     transmission.SmallestPendingPacketIndex = packet.PacketId;
            //     transmission.WindowLowerBoundIndex++;
            // }
            
               
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

        // protected void Dispose(bool disposing)
        // {
        //     _socketReceiver?.Dispose();
        //     _cancellationTokenSource.Dispose();
        //     
        //     base.Dispose(disposing);
        // }
    }
}