// using System;
// using System.Collections.Generic;
// using System.Net;
// using System.Net.Sockets;
// using System.Threading;
// using System.Threading.Tasks;
// using PBMultiplayerServer.Configuration;
// using PBMultiplayerServer.Core.Factories;
// using PBMultiplayerServer.Core.Messages.MessageHelper;
//
// namespace UdpTransport
// {
//     public class UdpConnection : Connection
//     {
//         private readonly ISocketProxy _socket;
//         private readonly INetworkConfiguration _networkConfiguration;
//         private EndPoint _remoteEndPoint;
//         private bool _running;
//         private readonly CancellationTokenSource _cancellationTokenSource = new();
//         private CancellationToken _cancellationToken;
//         private bool _disposedValue;
//         private readonly Queue<UdpTransmission> _transmissions = new();
//         private readonly Queue<Packet> _packets = new();
//         private UdpTransmission currentTransmission;
//
//         public UdpConnection(
//             IPEndPoint remoteEndPoint, 
//             ISocketProxy socket,
//             INetworkConfiguration networkConfiguration) : base(remoteEndPoint)
//         {
//             _socket = socket;
//         }
//
//         public void StartReceive()
//         {
//             if(_running)
//                 CloseConnection();
//             
//             _running = true;
//             _cancellationToken = _cancellationTokenSource.Token;
//         }
//
//         public async Task ReceiveAsync()
//         {
//             _cancellationToken = _cancellationTokenSource.Token;
//             
//             var data = new byte[1024];
//             var iEndpoint = new IPEndPoint(IPAddress.Any, 0);
//             try
//             {
//                 while (!_cancellationToken.IsCancellationRequested)
//                 {
//                     var receiveFromResult = await _socket.ReceiveFromAsync(data, SocketFlags.None, iEndpoint);
//                     OnDataReceived(data, receiveFromResult.ReceivedBytes, (IPEndPoint)receiveFromResult.RemoteEndPoint);
//                 }
//             }
//             catch (Exception e)
//             {
//                 Console.WriteLine(e);
//                 throw;
//             }
//         }
//
//         public void Receive()
//         {
//             if(!_running)
//                 return;
//             
//             try
//             {
//                 var canReceive = true;
//                 
//                 var buffer = new byte[1024];
//                 var byteReceived = 0;
//                 
//                 while (canReceive && !_cancellationToken.IsCancellationRequested)
//                 {
//                     if (_socket.Available > 0 && _socket.Poll(0, SelectMode.SelectRead))
//                     {
//                         byteReceived = _socket.ReceiveFrom(buffer, SocketFlags.None, ref _remoteEndPoint);
//                     }
//                     else
//                     {
//                         canReceive = false;
//                     }
//                     
//                     if(byteReceived > 0)
//                         OnDataReceived(buffer, byteReceived, (IPEndPoint)_remoteEndPoint);
//                 }
//             }
//             catch (Exception e)
//             {
//                 Console.WriteLine(e);
//                 throw;
//             }
//         }
//
//         public override void CloseConnection()
//         {
//             _cancellationTokenSource.Cancel();
//             _running = false;
//             _socket.Close();
//         }
//
//         public override void Send(byte[] data)
//         {
//             _socket.Send(data);
//         }
//         
//         public async Task SendEvents()
//         {
//             var maxSendTime = DateTime.Now.AddMilliseconds(100);
//
//             var windowUpperBound = currentTransmission.WindowLowerBoundIndex + currentTransmission.WindowSize;
//             
//             for (var i = currentTransmission.WindowLowerBoundIndex;
//                  i < windowUpperBound && DateTime.Now < maxSendTime; 
//                  i++)
//             {
//                 var packet = currentTransmission.Packets[i];
//
//                 if (packet.ResendTime <= DateTime.Now && !packet.HasAck)
//                 {
//                     packet.ResendTime = DateTime.Now.AddMilliseconds(500);
//                 
//                     packet.ResendAttemptCount++;
//                 
//                     await SendAsync(packet.Payload);
//                 }
//             }
//         }
//         
//         public void CreateTransmission(byte[] data)
//         {
//             var id = NetworkMessageHelper.GetTransmissionId(data);
//
//             var transmission = new UdpTransmission()
//             {
//                 Id = id,
//                 WindowSize = 5,
//                 WindowLowerBoundIndex = 0,
//                 SmallestPendingPacketIndex = 0,
//             };
//             
//             _transmissions.Enqueue(transmission);
//         }
//         
//         public void UpdateTimeOuts()
//         {
//             var windowUpperBound = currentTransmission.WindowLowerBoundIndex + currentTransmission.WindowSize;
//             
//             for (var i = currentTransmission.WindowLowerBoundIndex;
//                  i < windowUpperBound; 
//                  i++)
//             {
//                 var packet = currentTransmission.Packets[i];
//
//                 if (packet.ResendAttemptCount > _networkConfiguration.MaxPacketResendCount)
//                 {
//                     CloseConnection();
//                 }
//             }
//         }
//         
//         public void HandleAck(ushort packetId)
//         {
//             var windowUpperBound = currentTransmission.WindowLowerBoundIndex + currentTransmission.WindowSize;
//             
//             //packet doesnt belongs to current window
//             if(packetId < currentTransmission.WindowLowerBoundIndex || packetId > windowUpperBound - 1)
//                 return;
//
//             var packet = currentTransmission.Packets[packetId];
//             
//             packet.HasAck = true;
//
//             if (packetId == currentTransmission.SmallestPendingPacketIndex)
//                 ShiftWindow();
//         }
//         
//         private bool IsPacketBlockAcked()
//         {
//             var windowUpperBound = currentTransmission.WindowLowerBoundIndex + currentTransmission.WindowSize;
//             
//             for (var i = currentTransmission.WindowLowerBoundIndex;
//                  i < windowUpperBound; 
//                  i++)
//             {
//                 var packet = currentTransmission.Packets[i];
//
//                 if (!packet.HasAck)
//                     return false;
//             }
//
//             return true;
//         }
//         
//         private void ShiftWindow()
//         {
//             var smallestUnAckedPacket = currentTransmission.SmallestPendingPacketIndex;
//             
//             var windowUpperBound = currentTransmission.WindowLowerBoundIndex + currentTransmission.WindowSize;
//             
//             ushort diff = 0;
//             for (diff = (ushort)(smallestUnAckedPacket + 1);
//                  diff < windowUpperBound + 1; 
//                  diff++)
//             {
//                 var packet = currentTransmission.Packets[diff];
//
//                 if (!packet.HasAck)
//                 {
//                     currentTransmission.SmallestPendingPacketIndex = packet.PacketId;
//                     
//                     break;
//                 }
//                 
//                 currentTransmission.WindowLowerBoundIndex = diff;
//             }
//         }
//
//         public override async Task SendAsync(byte[] data)
//         {
//             await _socket.SendAsync(data);
//         }
//         
//         public Task SendToAsync(byte[] data, IPEndPoint remoteEndpoint)
//         {
//             return _socket.SendToAsync(data, remoteEndpoint);
//         }
//
//         protected override void Dispose(bool disposing)
//         {
//             if (!_disposedValue)
//             {
//                 if (disposing)
//                 {
//                     _socket.Dispose();
//                     _cancellationTokenSource.Dispose();
//                 }
//
//                 _disposedValue = true;
//             }
//
//             // Call base class implementation.
//             base.Dispose(disposing);
//         }
//     }
// }