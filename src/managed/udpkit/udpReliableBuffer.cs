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

using System;

namespace UdpKit {
    public enum UdpReliableRecvResult {
        Old,
        OutOfBounds,
        AlreadyExists,
        Added
    }

    public class UdpReliableBuffer<T> where T : IUdpSequencedObject {
        UdpReliableRecvQueue<T> recv;
        UdpReliableSendQueue<T> send;

        public int SequenceBits {
            get;
            private set;
        }

        public UdpReliableBuffer (uint windowSize) {
            if (windowSize < (1 << 2)) throw new ArgumentException("Must be >= (1 << 2)", "windowSize");
            if (windowSize > (1 << 14)) throw new ArgumentException("Must be <= (1 << 14)", "windowSize");
            if (UdpMath.IsPowerOfTwo(windowSize) == false) throw new ArgumentException("Must be a power of two", "windowSize");

            SequenceBits = UdpMath.HighBit(windowSize) + 2;

            recv = new UdpReliableRecvQueue<T>(SequenceBits);
            send = new UdpReliableSendQueue<T>(SequenceBits);
        }

        public bool Send_TryEnqueue (T value) {
            return send.TryQueueForSending(value);
        }

        public bool Send_TryGetForPacking (out T value) {
            return send.TryGetForPacking(out value);
        }

        public bool Send_TryGetForRemoval (out T value) {
            return send.TryRemoveDelivered(out value);
        }

        // added by Erhune
        public void SetIsDelivered(T value)
        {
            send.SetIsDelivered(value);
        }

        // added by Erhune
        public void SetSendAgain(T value)
        {
            send.SetSendAgain(value);
        }

        public bool Recv_TryEnqueue (T value, out UdpReliableRecvResult result) {
            return recv.TryEnqueueForDelivery(value, out result);
        }

        public bool Recv_TryDeliver (out T value) {
            return recv.TryGetForDelivery(out value);
        }
    }
}
