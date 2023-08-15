namespace PBUdpTransport.Utils
{
    internal enum EPacketFlags : ushort
    {
        FirstPacket,
        LastPacket,
        Ack,
        RequestForPacket,
        Default
    }
}