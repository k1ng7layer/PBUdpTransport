using System;
using System.Numerics;
using System.Text;

namespace PBUdpTransport.Helpers
{
    internal class ByteWriter
    {
        public int WritePos { get; internal set; }
        
        public readonly byte[] Data;
        
        public ByteWriter(int length = 8)
        {
            Data = new byte[length];
        }
        
         public void AddInt(int value)
        {
            var bytes = BitConverter.GetBytes(value);

            Data[WritePos] = bytes[0];
            Data[WritePos + 1] = bytes[1];
            Data[WritePos + 2] = bytes[2];
            Data[WritePos + 3] = bytes[3];

            WritePos += sizeof(int);
        }
        
        public void AddFloat(float value)
        {
            var bytes = BitConverter.GetBytes(value);
            
            Data[WritePos] = bytes[0];
            Data[WritePos + 1] = bytes[1];
            Data[WritePos + 2] = bytes[2];
            Data[WritePos + 3] = bytes[3];
            
            WritePos += sizeof(float);
        }
        
        public void AddString(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            
            for (int i = 0; i < bytes.Length; i++)
            {
                Data[WritePos + i] = bytes[i];
            }
            
            WritePos += bytes.Length;
        }
        
        public void AddBytes(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                Data[WritePos + i] = bytes[i];
            }
            
            WritePos += bytes.Length;
        }
        
        public void AddUshort(ushort value)
        {
            var bytes = BitConverter.GetBytes(value);

            Data[WritePos] = bytes[0];
            Data[WritePos + 1] = bytes[1];

            WritePos += sizeof(ushort);
        }
        
        protected void AddBool(bool value)
        {
            var bytes = BitConverter.GetBytes(value);

            Data[WritePos] = bytes[0];

            WritePos += 1;
        }
        
        public void AddVector3(Vector3 value)
        {
            AddFloat(value.X);
            AddFloat(value.Y);
            AddFloat(value.Z);
        }
        
        public void AddQuaternion(Quaternion value)
        {
            AddFloat(value.X);
            AddFloat(value.Y);
            AddFloat(value.Z);
            AddFloat(value.W);
        }
    }
}