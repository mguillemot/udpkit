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
    public class UdpReliableSendQueue<T> where T : IUdpSequencedObject {
        enum State {
            Free = 0,
            Send = 1,
            Transit = 2,
            Delivered = 3
        }

        struct Node {
            public State State;
            public T Value;
        }

        struct SequenceGenerator {
            uint mask;
            uint sequence;

            public SequenceGenerator (int bits)
                : this(bits, 0u) {
            }

            public SequenceGenerator (int bits, uint start) {
                mask = (1u << bits) - 1u;
                sequence = start & mask;
            }

            public uint Next () {
                sequence += 1u;
                sequence &= mask;
                return sequence;
            }
        }

        int tail;
        int mask;
        int shift;
        int count;

        Node[] nodes;
        SequenceGenerator generator;

        public bool IsFull {
            get { return count == nodes.Length; }
        }

        public UdpReliableSendQueue (int sequenceBits) {
            if (sequenceBits < 4 || sequenceBits > 32) {
                throw new System.ArgumentException("sequenceBits must be >= 4 and <= 32");
            }

            nodes = new Node[1 << (sequenceBits - 2)];
            shift = 32 - sequenceBits;
            mask = nodes.Length - 1;
            generator = new SequenceGenerator(sequenceBits, uint.MaxValue);
        }

        public bool TryQueueForSending (T value) {
            int index = -1;

            if (count == 0) {
                index = tail;
            } else {
                if (count == nodes.Length) {
                    return false;
                }

                index = (tail + count) & mask;
            }

            nodes[index].Value = value;
            nodes[index].Value.sequence = generator.Next();
            nodes[index].State = State.Send;

            count += 1;
            return true;
        }

        public bool TryGetForPacking (out T value) {
            for (int i = 0; i < count; ++i) {
                int index = (tail + i) & mask;

                if (nodes[index].State == State.Send) {
                    nodes[index].State = State.Transit;
                    value = nodes[index].Value;
                    return true;
                }
            }

            value = default(T);
            return false;
        }

        public bool TryRemoveDelivered (out T value) {
            if (count > 0 && nodes[tail].State == State.Delivered) {
                value = nodes[tail].Value;
                nodes[tail] = default(Node);

                tail += 1;
                tail &= mask;

                count -= 1;
                return true;
            }

            value = default(T);
            return false;
        }

        public void SetIsDelivered (T value) {
            ChangeState(value, State.Delivered);
        }

        public void SetSendAgain (T value) {
            ChangeState(value, State.Send);
        }

        void ChangeState (T value, State state) {
            if (count == 0) {
                return;
            }

            // ERHUNE j'ai un null pointer ici !!!
            int distance = SequenceDistance(value.sequence, nodes[tail].Value.sequence, shift);
            if (distance < 0 || distance >= count) {
                return;
            }

            nodes[(tail + distance) & mask].State = state;
        }

        static int SequenceDistance (uint from, uint to, int shift) {
            from <<= shift;
            to <<= shift;
            return ((int) (from - to)) >> shift;
        }
    }
}
