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
using System.Collections.Generic;

namespace UdpKit {
#if DEBUG
    partial class UdpSocket {
        struct DelayedPacket {
            public UdpEndPoint EndPoint;
            public byte[] Data;
            public int Length;
            public uint Time;
        }

        readonly Queue<byte[]> delayedBuffers = new Queue<byte[]>();
        readonly Queue<DelayedPacket> delayedPackets = new Queue<DelayedPacket>();

        bool ShouldDelayPacket {
            get { return Config.SimulatedPingMin > 0 && Config.SimulatedPingMax > 0 && Config.SimulatedPingMin < Config.SimulatedPingMax; }
        }

        bool ShouldDropPacket {
            get {
                float n = Config.NoiseFunction();
                if (n > 0f && Config.NoiseFunction() < Config.SimulatedLoss) {
                    UdpLog.Debug("Dropping packet (Simulated)");
                    return true;
                } else {
                    return false;
                }
            }
        }

        partial void DelayPacket (UdpEndPoint ep, byte[] data, int length) {
            uint pingMin = (uint) Config.SimulatedPingMin;
            uint pingMax = (uint) Config.SimulatedPingMax;
            uint delay =  pingMin + (uint) ((pingMax - pingMin) * Config.NoiseFunction());

            DelayedPacket packet = new DelayedPacket();
            packet.Data = delayedBuffers.Count > 0 ? delayedBuffers.Dequeue() : new byte[Config.PacketSize * 2];
            packet.EndPoint = ep;
            packet.Length = length;
            packet.Time = GetCurrentTime() + delay;

            // copy entire buffer into packets data buffer
            Array.Copy(data, 0, packet.Data, 0, data.Length);

            // put on delay queue
            delayedPackets.Enqueue(packet);
        }

        partial void RecvDelayedPackets () {
            while (delayedPackets.Count > 0 && GetCurrentTime() >= delayedPackets.Peek().Time) {
                DelayedPacket packet = delayedPackets.Dequeue();
                UdpStream stream = GetReadStream();

                // copy data into streams buffer
                Array.Copy(packet.Data, 0, stream.Data, 0, packet.Data.Length);

                // clear packet data
                Array.Clear(packet.Data, 0, packet.Data.Length);

                // receive packet
                RecvNetworkPacket(packet.EndPoint, stream, packet.Length);

                // put packet data buffer back in pool
                delayedBuffers.Enqueue(packet.Data);
            }
        }
    }
#else
    partial class UdpSocket {
        bool ShouldDelayPacket {
            get { return false; }
        }

        bool ShouldDropPacket {
            get { return false; }
        }
    }
#endif
}
