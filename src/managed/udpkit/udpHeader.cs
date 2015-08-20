/*
* The MIT License (MIT)
* 
* Copyright (c) 2012-2014 Fredrik Holmstrom (fredrik.johan.holmstrom@gmail.com)
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*/

namespace UdpKit {
    public struct UdpHeader {

        // Erhune changes: sequence 15->16 bits, simplify padding, remove "object" notion from headers

        public const int SEQ_BITS = 16;
        public const int SEQ_PADD = 0; 
        public const int SEQ_MASK = (1 << SEQ_BITS) - 1;
        public const int NETPING_BITS = 16;

        public ushort ObjSequence;
        public ushort AckSequence;
        public ulong AckHistory;
        public ushort AckTime;
        //public bool IsObject;
        public uint Now;

        public void Pack (UdpStream buffer, UdpSocket socket) {
            int pos = buffer.Position;

            buffer.Position = 0;
            buffer.WriteUShort(ObjSequence, SEQ_BITS); // Erhune simplify
            buffer.WriteUShort(AckSequence, SEQ_BITS);
            buffer.WriteULong(AckHistory, UdpSocket.AckRedundancy);

            if (UdpSocket.CalculateNetworkPing) {
                buffer.WriteUShort(AckTime, NETPING_BITS);
            }

            buffer.Position = pos;
        }

        public void Unpack (UdpStream buffer) {
            buffer.Position = 0;

            ObjSequence = buffer.ReadUShort(SEQ_BITS);
            AckSequence = buffer.ReadUShort(SEQ_BITS);
            AckHistory = buffer.ReadULong(UdpSocket.AckRedundancy);

            if (UdpSocket.CalculateNetworkPing) {
                AckTime = buffer.ReadUShort(NETPING_BITS);
            }
        }

    }
}
