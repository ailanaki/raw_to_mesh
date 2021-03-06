using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace GeoGlobetrotterProtoRocktree
{
    public class DecoderRockTree
    {
// unpackVarInt unpacks variable length integer from proto (like coded_stream.h)
        private static int UnpackVarInt(ByteString packed, ref int index)
        {
            var data = packed.ToByteArray();
            var size = data.Length;
            int c = 0;
            int d = 1, e;
            if (index != size)
            {
                do
                {
                    e = data[index++];
                    c += Convert.ToInt32((e & 0x7F) * d);
                    d <<= 7;
                } while ((e & 0x80) != 0 && index != size);
            }

            return c;
        }

// vertex is a packed struct for an 8-byte-per-vertex array
        public class VertexT
        {
            public byte X, Y, Z; // position
            public byte W; // octant mask
            public byte U1, U2, V1, V2; // texture coordinates

            public VertexT(byte x, byte y, byte z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        };


// unpackVertices unpacks vertices XYZ to new 8-byte-per-vertex array
        public List<VertexT> UnpackVertices(ByteString packed)
        {
            var data = packed.Memory.ToArray();
            var count = packed.Length / 3;
            var vtx = new List<VertexT>(count);
            byte x = 0;
            byte y = 0;
            byte z = 0;
            // 8 bit for % 0x100;
            for (var i = 0; i < count; i++)
            {
                x += data[count * 0 + i];
                y += data[count * 1 + i];
                z += data[count * 2 + i];
                vtx.Add(new VertexT(x, y, z));
            }

            return vtx;
        }


// unpackTexCoords unpacks texture coordinates UV to 8-byte-per-vertex-array
        public List<VertexT> UnpackTexCoords(ByteString packed, List<VertexT> vertices, ref List<float> uvOffset, ref List<float> uvScale)
        {
            var data = new List<byte>(packed.ToByteArray());
            var uMod = 1 + data[0] + (data[1] << 8);
            var vMod = 1 + data[2] + (data[3] << 8);
            int u = 0, v = 0;
            var count = (data.Count - 4) / 4;
            for (var i = 0; i < count; i++)
            {
                u = (u + data[0 * count + i + 4] + (data[2 * count + i + 4] << 8)) % uMod;
                v = (v + data[1 * count + i + 4] + (data[3 * count + i + 4] << 8)) % vMod;
                
                vertices[i].U1 = Convert.ToByte(u & 255);
                vertices[i].U2 = Convert.ToByte(u >> 8);
                vertices[i].V1 = Convert.ToByte(v & 255);
                vertices[i].V2 = Convert.ToByte(v >> 8);
            }
            uvScale[0] = (float) 1.0 / uMod;
            uvScale[1] = (float) 1.0 / vMod;
            uvOffset[0] = (float) 0.5;
            uvOffset[1] = (float) 0.5 - 1 / uvScale[1];
            uvScale[1] *= -1;
            
            return vertices;
        }

// unpackIndices unpacks indices to triangle strip
        public List<int> UnpackIndices(ByteString packed)
        {
            var offset = 0;
            var triangleStripLen = UnpackVarInt(packed, ref offset);
            var triangleStrip = new List<int>(triangleStripLen);
            for (int i = 0; i < triangleStripLen; i++)
            {
                triangleStrip.Add(0);
            }
            for (int i = 0, zeros = 0, c = 0; i < triangleStripLen; i += 1)
            {
                var res = UnpackVarInt(packed, ref offset);
                c = zeros - res;
                triangleStrip[i] = Convert.ToUInt16(c);
                if (0 == res) zeros++;
            }

            return triangleStrip;
        }


// unpackOctantMaskAndOctantCountsAndLayerBounds unpacks the octant mask for vertices (W) and layer bounds and octant counts
        public int[] UnpackOctantMaskAndOctantCountsAndLayerBounds(ByteString packed, List<int> indices,
            List<VertexT> vertices)
        {
            var offset = 0;
            var len = UnpackVarInt(packed, ref offset);
            var idxI = 0;
            var k = 0;
            var m = 0;
            if (len < 10) len = 10;
            var layerBounds = new int[len];

            for (var i = 0; i < len; i++)
            {
                if (0 == i % 8)
                {
                    layerBounds[m++] = k;
                }

                var v = UnpackVarInt(packed, ref offset);
                for (var j = 0; j < v; j++)
                {
                    if (idxI < indices.Count)
                    {
                        var idx = indices[idxI++];
                        vertices[idx].W = Convert.ToByte(i & 7);
                    }
                }

                k += v;
            }

            for (; 10 > m; m++) layerBounds[m] = k;
            return layerBounds;
        }


        public class ResultForNormals
        {
            public ResultForNormals(byte[] unpackedForNormals, int count)
            {
                UnpackedForNormals = unpackedForNormals;
                Count = count;
            }

            public byte[] UnpackedForNormals;
            public int Count;
        }

// unpackForNormals unpacks normals info for later mesh normals usage
        public ResultForNormals UnpackForNormals(NodeData nodeData)
        {
            int f1(int v, int l)
            {
                if (4 >= l)
                    return (v << l) + (v & (1 << l) - 1);
                if (6 >= l)
                {
                    var r = 8 - l;
                    return (v << l) + (v << l >> r) + (v << l >> r >> r) + (v << l >> r >> r >> r);
                }

                return -(v & 1);
            }

            ;

            int f2(double c)
            {
                var cr = (int) Math.Round(c);
                if (cr < 0) return 0;
                if (cr > 255) return 255;
                return cr;
            }

            var input = nodeData.ForNormals;
            var data = new List<byte>(input.ToByteArray());
            var count = data[0] + (data[1] << 8);
            int s = data[2];
            data.Remove(data[0]);
            data.Remove(data[0]);
            data.Remove(data[0]);
            data.Add(0);
            data.Add(0);
            data.Add(0);

            var output = new byte[3 * count];

            for (var i = 0; i < count; i++)
            {
                double a = f1(data[0 + i], s) / 255.0;
                double f = f1(data[count + i], s) / 255.0;

                double b = a, c = f, g = b + c, h = b - c;
                int sign = 1;

                if (!(.5 <= g && 1.5 >= g && -.5 <= h && .5 >= h))
                {
                    sign = -1;
                    if (.5 >= g)
                    {
                        b = .5 - f;
                        c = .5 - a;
                    }
                    else
                    {
                        if (1.5 <= g)
                        {
                            b = 1.5 - f;
                            c = 1.5 - a;
                        }
                        else
                        {
                            if (-.5 >= h)
                            {
                                b = f - .5;
                                c = a + .5;
                            }
                            else
                            {
                                b = f + .5;
                                c = a - .5;
                            }
                        }
                    }

                    g = b + c;
                    h = b - c;
                }

                a = Math.Min(Math.Min(2 * g - 1, 3 - 2 * g), Math.Min(2 * h + 1, 1 - 2 * h)) * sign;
                b = 2 * b - 1;
                c = 2 * c - 1;
                var m = 127 / Math.Sqrt(a * a + b * b + c * c);

                output[3 * i + 0] = Convert.ToByte(f2(m * a + 127));
                output[3 * i + 1] = Convert.ToByte(f2(m * b + 127));
                output[3 * i + 2] = Convert.ToByte(f2(m * c + 127));
            }

            return new ResultForNormals(output, 3 * count);
        }

// unpackNormals unpacks normals indices in mesh using normal data from NodeData
        public ResultForNormals UnpackNormals(Mesh mesh, byte[] unpackedForNormals, int isUn)
        {
            var normals = mesh.Normals;
            byte[] newNormals;
            int count = 0;
            if (mesh.HasNormals && isUn != 0)
            {
                var input = normals.ToByteArray();
                count = normals.Length / 2;
                newNormals = new byte[4 * count];
                for (var i = 0; i < count; ++i)
                {
                    int j = input[i] + (input[count + i] << 8);
                    newNormals[4 * i + 0] = unpackedForNormals[3 * j + 0];
                    newNormals[4 * i + 1] = unpackedForNormals[3 * j + 1];
                    newNormals[4 * i + 2] = unpackedForNormals[3 * j + 2];
                    newNormals[4 * i + 3] = 0;
                }
            }
            else
            {
                count = (mesh.Vertices.Length / 3) * 8;
                newNormals = new byte[4 * count];
                for (var i = 0; i < count; ++i)
                {
                    newNormals[4 * i + 0] = 127;
                    newNormals[4 * i + 1] = 127;
                    newNormals[4 * i + 2] = 127;
                    newNormals[4 * i + 3] = 0;
                }
            }

            return new ResultForNormals(newNormals, 4 * count);
        }

        public struct NodeDataPathAndFlagsT
        {
            public char[] Path;
            public int Flags;
            public int Level;

            public NodeDataPathAndFlagsT(int flags, int level)
            {
                Path = new char[21];
                Flags = flags;
                Level = level;
            }

            public string getPath()
            {
                string preString = new string(Path);
                int i = preString.Length - 1;
                while (preString[i] == '\0')
                {
                    i--;
                }

                return preString.Substring(0, i);
            }
        };

// unpackPathAndFlags unpacks path, flags and level (strlen(path)) from node metadata
        public NodeDataPathAndFlagsT UnpackPathAndFlags(NodeMetadata nodeMeta)
        {
            NodeDataPathAndFlagsT GetPathAndFlags(int pathId)
            {
                var level = 1 + (pathId & 3);
                var result = new NodeDataPathAndFlagsT(pathId, level);
                pathId >>= 2;
                for (int i = 0; i < level; i++)
                {
                    result.Path[i] = Convert.ToChar(48 + (pathId & 7));
                    pathId >>= 3;
                }

                result.Flags = pathId;
                return result;
            }

            NodeDataPathAndFlagsT result = GetPathAndFlags((int) nodeMeta.PathAndFlags);
            // result.Path[result.Level] = '\0';
            Console.WriteLine(result.getPath());
            return result;
        }
    }
}