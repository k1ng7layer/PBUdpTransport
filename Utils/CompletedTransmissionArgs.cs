using PBUdpTransport.Models;

namespace PBUdpTransport.Utils
{
    internal class CompletedTransmissionArgs
    {
        public readonly TransportMessage TransportMessage;
        public readonly bool IsSuccessfullyCompleted;

        public CompletedTransmissionArgs(TransportMessage transportMessage, bool isSuccessfullyCompleted)
        {
            TransportMessage = transportMessage;
            IsSuccessfullyCompleted = isSuccessfullyCompleted;
        }
    }
}