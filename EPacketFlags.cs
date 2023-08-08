namespace UdpTransport
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