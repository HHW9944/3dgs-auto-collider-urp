using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GsPhysicsToolkit.Core
{
    [CreateAssetMenu(fileName = "NewSplatData", menuName = "3DGS/Splat Data Asset")]
    public class SplatData : ScriptableObject
    {
        private const float SH_C0 = 0.28209479177387814f;

        private struct PlyProperty
        {
            public string type;
            public string name;
        }

        public Vector3[] Positions;
        public Vector3[] Axes;
        public Color[] Colors;

        public int Count => Positions != null ? Positions.Length : 0;

        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs, Encoding.UTF8))
            {
                int vertexCount = 0;
                List<PlyProperty> properties = new List<PlyProperty>();
                string line;

                while ((line = ReadLineHeader(br)) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("element vertex")) vertexCount = int.Parse(line.Split(' ')[2]);
                    if (line.StartsWith("property"))
                    {
                        string[] tokens = line.Split(' ');
                        if (tokens.Length >= 3)
                        {
                            properties.Add(new PlyProperty { type = tokens[1], name = tokens[2] });
                        }
                    }
                    if (line == "end_header") break;
                }

                Positions = new Vector3[vertexCount];
                Colors = new Color[vertexCount];
                Axes = new Vector3[vertexCount * 3];

                bool isFullGaussian = HasProperty(properties, "opacity")
                    && HasProperty(properties, "scale_0")
                    && HasProperty(properties, "scale_1")
                    && HasProperty(properties, "scale_2")
                    && HasProperty(properties, "rot_0")
                    && HasProperty(properties, "rot_1")
                    && HasProperty(properties, "rot_2")
                    && HasProperty(properties, "rot_3");

                for (int i = 0; i < vertexCount; i++)
                {
                    Dictionary<string, float> values = ReadVertexProperties(br, properties);

                    float px = GetValue(values, "x");
                    float py = GetValue(values, "y");
                    float pz = GetValue(values, "z");
                    Positions[i] = new Vector3(-px, -py, -pz);

                    if (isFullGaussian)
                    {
                        float f_dc_0 = GetValue(values, "f_dc_0");
                        float f_dc_1 = GetValue(values, "f_dc_1");
                        float f_dc_2 = GetValue(values, "f_dc_2");

                        float opacity = Sigmoid(GetValue(values, "opacity"));
                        float sx = Mathf.Exp(Mathf.Clamp(GetValue(values, "scale_0"), -20.0f, 20.0f));
                        float sy = Mathf.Exp(Mathf.Clamp(GetValue(values, "scale_1"), -20.0f, 20.0f));
                        float sz = Mathf.Exp(Mathf.Clamp(GetValue(values, "scale_2"), -20.0f, 20.0f));
                        float rw = GetValue(values, "rot_0");
                        float rx = GetValue(values, "rot_1");
                        float ry = GetValue(values, "rot_2");
                        float rz = GetValue(values, "rot_3");

                        Quaternion rot = new Quaternion(-rx, -ry, rz, rw).normalized;
                        Axes[i * 3 + 0] = rot * new Vector3(sx, 0f, 0f);
                        Axes[i * 3 + 1] = rot * new Vector3(0f, sy, 0f);
                        Axes[i * 3 + 2] = rot * new Vector3(0f, 0f, sz);

                        Colors[i] = new Color(
                            Mathf.Clamp01(0.5f + SH_C0 * f_dc_0),
                            Mathf.Clamp01(0.5f + SH_C0 * f_dc_1),
                            Mathf.Clamp01(0.5f + SH_C0 * f_dc_2),
                            opacity
                        );
                    }
                    else
                    {
                        float r = GetColorValue(values, "red", "r");
                        float g = GetColorValue(values, "green", "g");
                        float b = GetColorValue(values, "blue", "b");
                        Colors[i] = new Color(r, g, b, 1.0f);
                        Axes[i * 3 + 0] = Vector3.zero;
                        Axes[i * 3 + 1] = Vector3.zero;
                        Axes[i * 3 + 2] = Vector3.zero;
                    }
                }

                Debug.Log($"[PLY] Loaded {(isFullGaussian ? "3DGS" : "Point Cloud")} data: {vertexCount} vertices");
            }
        }

        private bool HasProperty(List<PlyProperty> properties, string name)
        {
            for (int i = 0; i < properties.Count; i++)
            {
                if (properties[i].name == name) return true;
            }

            return false;
        }

        private Dictionary<string, float> ReadVertexProperties(BinaryReader br, List<PlyProperty> properties)
        {
            Dictionary<string, float> values = new Dictionary<string, float>(properties.Count);

            for (int i = 0; i < properties.Count; i++)
            {
                PlyProperty property = properties[i];
                values[property.name] = ReadPropertyValue(br, property.type);
            }

            return values;
        }

        private float ReadPropertyValue(BinaryReader br, string type)
        {
            switch (type)
            {
                case "float":
                case "float32":
                    return br.ReadSingle();
                case "double":
                case "float64":
                    return (float)br.ReadDouble();
                case "uchar":
                case "uint8":
                    return br.ReadByte();
                case "char":
                case "int8":
                    return br.ReadSByte();
                case "ushort":
                case "uint16":
                    return br.ReadUInt16();
                case "short":
                case "int16":
                    return br.ReadInt16();
                case "uint":
                case "uint32":
                    return br.ReadUInt32();
                case "int":
                case "int32":
                    return br.ReadInt32();
                default:
                    Debug.LogWarning($"Unsupported PLY property type: {type}. Reading as float.");
                    return br.ReadSingle();
            }
        }

        private float GetValue(Dictionary<string, float> values, string key, float defaultValue = 0.0f)
        {
            return values.TryGetValue(key, out float value) ? value : defaultValue;
        }

        private float GetColorValue(Dictionary<string, float> values, string longName, string shortName)
        {
            float value = values.TryGetValue(longName, out float longValue)
                ? longValue
                : GetValue(values, shortName, 255.0f);

            return value > 1.0f ? value / 255.0f : value;
        }

        private float Sigmoid(float value)
        {
            value = Mathf.Clamp(value, -20.0f, 20.0f);
            return 1.0f / (1.0f + Mathf.Exp(-value));
        }

        private string ReadLineHeader(BinaryReader br)
        {
            StringBuilder sb = new StringBuilder();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public SplatData CreatePhysicsFilteredData(
            Bounds filterBounds,
            bool includeInside,
            bool useAxisSamples,
            float axisSampleScale
        )
        {
            SplatData filteredData = CreateInstance<SplatData>();
            filteredData.name = $"{name}_PhysicsFiltered";

            List<Vector3> filteredPositions = new List<Vector3>();
            List<Color> filteredColors = new List<Color>();
            List<Vector3> filteredAxes = new List<Vector3>();

            for (int i = 0; i < Count; i++)
            {
                Vector3 position = Positions[i];
                bool isInside = filterBounds.Contains(position);

                if (isInside != includeInside) continue;

                Color color = Colors != null && i < Colors.Length ? Colors[i] : Color.white;

                AddPhysicsPoint(position, color, i, filteredPositions, filteredColors, filteredAxes, true);

                if (useAxisSamples && Axes != null && Axes.Length >= i * 3 + 3)
                {
                    Vector3 axisX = Axes[i * 3 + 0] * axisSampleScale;
                    Vector3 axisY = Axes[i * 3 + 1] * axisSampleScale;
                    Vector3 axisZ = Axes[i * 3 + 2] * axisSampleScale;

                    AddPhysicsPoint(position + axisX, color, i, filteredPositions, filteredColors, filteredAxes, false);
                    AddPhysicsPoint(position - axisX, color, i, filteredPositions, filteredColors, filteredAxes, false);
                    AddPhysicsPoint(position + axisY, color, i, filteredPositions, filteredColors, filteredAxes, false);
                    AddPhysicsPoint(position - axisY, color, i, filteredPositions, filteredColors, filteredAxes, false);
                    AddPhysicsPoint(position + axisZ, color, i, filteredPositions, filteredColors, filteredAxes, false);
                }
            }

            filteredData.Positions = filteredPositions.ToArray();
            filteredData.Colors = filteredColors.ToArray();
            filteredData.Axes = filteredAxes.ToArray();

            Debug.Log($"Physics 필터링 완료: 원본 {Count}개 -> 전달 {filteredData.Count}개");

            return filteredData;
        }

        private void AddPhysicsPoint(
            Vector3 position,
            Color color,
            int sourceIndex,
            List<Vector3> positions,
            List<Color> colors,
            List<Vector3> axes,
            bool keepOriginalAxes
        )
        {
            positions.Add(position);
            colors.Add(color);

            if (keepOriginalAxes && Axes != null && Axes.Length >= sourceIndex * 3 + 3)
            {
                axes.Add(Axes[sourceIndex * 3 + 0]);
                axes.Add(Axes[sourceIndex * 3 + 1]);
                axes.Add(Axes[sourceIndex * 3 + 2]);
            }
            else
            {
                axes.Add(Vector3.zero);
                axes.Add(Vector3.zero);
                axes.Add(Vector3.zero);
            }
        }
    }
}
