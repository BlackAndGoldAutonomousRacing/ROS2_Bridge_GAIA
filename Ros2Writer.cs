/**
 * Copyright (c) 2020 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System;
using System.Diagnostics;
using System.Text;
using Simulator.Bridge.Data;
using Simulator.Bridge.Data.Ros;

namespace Simulator.Bridge.Ros2
{
    public class Ros2Writer<BridgeType>
    {
        Ros2BridgeInstance Instance;
        byte[] Topic;

        public Ros2Writer(Ros2BridgeInstance instance, string topic)
        {
            Instance = instance;
            Topic = Encoding.ASCII.GetBytes(topic);
        }

        public void Write(BridgeType message, Action completed)
        {
            int topicLength = Topic.Length;
            int messageLength = Ros2Serialization.GetLength(message);

            var bytes = new byte[1 + 4 + topicLength + 4 + messageLength];
            bytes[0] = (byte)BridgeOp.Publish;

            bytes[1] = (byte)(topicLength >> 0);
            bytes[2] = (byte)(topicLength >> 8);
            bytes[3] = (byte)(topicLength >> 16);
            bytes[4] = (byte)(topicLength >> 24);
            Buffer.BlockCopy(Topic, 0, bytes, 5, topicLength);

            bytes[5 + topicLength] = (byte)(messageLength >> 0);
            bytes[6 + topicLength] = (byte)(messageLength >> 8);
            bytes[7 + topicLength] = (byte)(messageLength >> 16);
            bytes[8 + topicLength] = (byte)(messageLength >> 24);
            int written = Ros2Serialization.Serialize(message, bytes, 9 + topicLength);

            Debug.Assert(written == messageLength, "Something is terribly wrong if this is not true");

            Instance.SendAsync(bytes, completed);
        }
    }

    class Ros2PointCloudWriter
    {
        Ros2Writer<PointCloud2> Writer;

        byte[] Buffer;

        const uint pointStep = 16;

        static readonly PointField[] PointFields = new[]
        {
            new PointField()
            {
                name = "x",
                offset = 0,
                datatype = PointField.FLOAT32,
                count = 1,
            },
            new PointField()
            {
                name = "y",
                offset = 4,
                datatype = PointField.FLOAT32,
                count = 1,
            },
            new PointField()
            {
                name = "z",
                offset = 8,
                datatype = PointField.FLOAT32,
                count = 1,
            },
            new PointField()
            {
                name = "intensity",
                offset = 12,
                datatype = PointField.FLOAT32,
                count = 1,
            },
            /*
            new PointField()
            {
                name = "intensity",
                offset = 16,
                datatype = PointField.UINT8,
                count = 1,
            },
            new PointField()
            {
                name = "timestamp",
                offset = 24,
                datatype = PointField.FLOAT64,
                count = 1,
            },
            */
        };

        public Ros2PointCloudWriter(Ros2BridgeInstance instance, string topic)
        {
            Writer = new Ros2Writer<PointCloud2>(instance, topic);
        }

        public void Write(PointCloudData data, Action completed)
        {
            if (Buffer == null || Buffer.Length != pointStep * data.Points.Length)
            {
                // Buffer = new byte[32 * data.Points.Length];
                Buffer = new byte[pointStep * data.Points.Length];
            }

            int count = 0;
            unsafe
            {
                fixed (byte* ptr = Buffer)
                {
                    int offset = 0;
                    for (int i = 0; i < data.Points.Length; i++)
                    {
                        var point = data.Points[i];
                        if (point == UnityEngine.Vector4.zero)
                        {
                            continue;
                        }

                        var pos = new UnityEngine.Vector3(point.x, point.y, point.z);
                        *(UnityEngine.Vector3*)(ptr + offset) = data.Transform.MultiplyPoint3x4(pos);
                        float intensity = point.w;
                        // *(ptr + offset + 16) = (byte)(intensity * 255);
                        *(float*)(ptr + offset + 12) = intensity;

                        // offset += 32;
                        offset += (int)pointStep;

                        count++;
                    }
                }
            }

            var msg = new PointCloud2()
            {
                header = new Header()
                {
                    stamp = Ros2Conversions.Convert(data.Time),
                    frame_id = data.Frame,
                },
                height = 1,
                width = (uint)count,
                fields = PointFields,
                is_bigendian = false,
                // point_step = 32,
                point_step = pointStep,
                row_step = (uint)count * pointStep,
                data = new PartialByteArray()
                {
                    Array = Buffer,
                    Length = count * (int)pointStep,
                },
                is_dense = true,
            };

            Writer.Write(msg, completed);
        }
    }
}

