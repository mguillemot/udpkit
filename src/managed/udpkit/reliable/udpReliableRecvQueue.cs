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
using System.Collections;
using System.Collections.Generic;

namespace UdpKit {
    public class UdpReliableRecvQueue<T> : IEnumerable<T> where T : IUdpSequencedObject {
        struct Node {
            public bool Received;
            public T Value;
        }

        int tail;
        int mask;
        int sequenceShift;
        uint sequenceNext;
        uint sequenceMask;
        readonly Node[] nodes;

        public UdpReliableRecvQueue (int sequenceBits) {
            if (sequenceBits < 4 || sequenceBits > 32) {
                throw new ArgumentException("sequenceBits must be >= 4 and <= 32");
            }

            nodes = new Node[1 << (sequenceBits - 2)];
            mask = nodes.Length - 1;

            sequenceShift = 32 - sequenceBits;
            sequenceMask = (1u << sequenceBits) - 1u;
            sequenceNext = 0;
        }

        public bool TryGetForDelivery (out T value) {
            Node n = nodes[tail];

            if (n.Received) {
                value = n.Value;
                nodes[tail] = default(Node);

                tail += 1;
                tail &= mask;

                sequenceNext = value.sequence + 1u;
                sequenceNext &= sequenceMask;
            } else {
                value = default(T);
            }

            return n.Received;
        }

        public bool TryEnqueueForDelivery (T value, out UdpReliableRecvResult result) {
            int distance = SequenceDistance(value.sequence, sequenceNext, sequenceShift);
            int index = (tail + distance) & mask;

            if (distance <= -nodes.Length || distance >= nodes.Length) {
                result = UdpReliableRecvResult.OutOfBounds;
                return false;
            }

            if (distance < 0) {
                result = UdpReliableRecvResult.Old;
                return false;
            }

            if (nodes[index].Received) {
                result = UdpReliableRecvResult.AlreadyExists;
                return false;
            }

            nodes[index].Received = true;
            nodes[index].Value = value;

            result = UdpReliableRecvResult.Added;
            return true;
        }

        static int SequenceDistance (uint from, uint to, int shift) {
            from <<= shift;
            to <<= shift;
            return ((int) (from - to)) >> shift;
        }

        // Erhune: added
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Erhune: added
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0, n = nodes.Length; i < n; i++)
            {
                var node = nodes[i];
                if (node.Received)
                {
                    yield return node.Value;
                }
            }
        }

        // Erhune: added
        public void Clear()
        {
            for (int i = 0, n = nodes.Length; i < n; i++)
            {
                nodes[i] = default(Node);
            }
        }
    }
}
