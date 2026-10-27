using GsPhysicsToolkit.Core;
using GsPhysicsToolkit.Rendering;
using System.Runtime.InteropServices;
using UnityEngine;

[ExecuteAlways]
public class GaussianSplat : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Gaussian
    {
        public Vector4 pos;
        public Vector4 color;
        public Vector4 axis0;
        public Vector4 axis1;
        public Vector4 axis2;
    }

    public SplatData data;
    public Material splatMaterial;
    [Min(0.1f)] public float splatScale = 1.0f;
    [Min(0.0f)] public float minScreenRadius = 4.0f;
    [Min(0.01f)] public float maxScreenRadius = 256.0f;
    public bool forceScreenRadius = false;
    [Min(0.1f)] public float forcedScreenRadius = 8.0f;
    public bool overrideMaterialProperties = false;
    public bool sortBackToFront = false;
    [Min(1)] public int maxCpuSortCount = 200000;
    [Min(0.01f)] public float sortInterval = 0.25f;

    private ComputeBuffer _splatBuffer;
    private Gaussian[] _gaussians;
    private int _count;
    private float _nextSortTime;
    private Vector3 _lastSortCameraPosition;
    private Quaternion _lastSortCameraRotation;

    private void OnEnable()
    {
        if (data != null && _splatBuffer == null)
        {
            Initialize(data);
        }
    }

    private void OnValidate()
    {
        splatScale = Mathf.Max(0.1f, splatScale);
        minScreenRadius = Mathf.Max(0.0f, minScreenRadius);
        maxScreenRadius = Mathf.Max(0.01f, maxScreenRadius);
        forcedScreenRadius = Mathf.Max(0.1f, forcedScreenRadius);
        maxCpuSortCount = Mathf.Max(1, maxCpuSortCount);
        sortInterval = Mathf.Max(0.01f, sortInterval);
    }

    private void Start()
    {
        if (data != null && _splatBuffer == null) Initialize(data);
    }

    public void Initialize(SplatData splatData)
    {
        data = splatData;
        if (data == null || data.Count == 0) return;

        _count = data.Count;
        _gaussians = new Gaussian[_count];

        for (int i = 0; i < _count; i++)
        {
            Vector3 axis0 = GetAxis(i, 0);
            Vector3 axis1 = GetAxis(i, 1);
            Vector3 axis2 = GetAxis(i, 2);

            _gaussians[i] = new Gaussian
            {
                pos = new Vector4(data.Positions[i].x, data.Positions[i].y, data.Positions[i].z, 1.0f),
                color = (Vector4)data.Colors[i],
                axis0 = new Vector4(axis0.x, axis0.y, axis0.z, 0.0f),
                axis1 = new Vector4(axis1.x, axis1.y, axis1.z, 0.0f),
                axis2 = new Vector4(axis2.x, axis2.y, axis2.z, 0.0f)
            };
        }

        if (_splatBuffer != null) _splatBuffer.Release();
        _splatBuffer = new ComputeBuffer(_count, Marshal.SizeOf<Gaussian>());
        _splatBuffer.SetData(_gaussians);

        Debug.Log($"[3DGS] Renderer initialized: {_count} gaussian splats");
    }

    private Vector3 GetAxis(int gaussianIndex, int axisIndex)
    {
        int index = gaussianIndex * 3 + axisIndex;
        if (data.Axes == null || index >= data.Axes.Length)
        {
            return Vector3.zero;
        }

        return data.Axes[index];
    }

    private void OnRenderObject()
    {
        Draw(Camera.current);
    }

    private void OnDestroy()
    {
        if (_splatBuffer != null)
        {
            _splatBuffer.Release();
            _splatBuffer = null;
        }
    }

    public void Draw(Camera cam)
    {
        if (_splatBuffer == null && data != null)
        {
            Initialize(data);
        }

        if (_splatBuffer == null || splatMaterial == null || cam == null) return;

        if (ShouldSort(cam))
        {
            SplatSorter.SortBackToFront(_gaussians, transform.localToWorldMatrix, cam);
            _splatBuffer.SetData(_gaussians);
            _lastSortCameraPosition = cam.transform.position;
            _lastSortCameraRotation = cam.transform.rotation;
            _nextSortTime = Time.realtimeSinceStartup + sortInterval;
        }

        splatMaterial.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
        splatMaterial.SetBuffer("_SplatBuffer", _splatBuffer);

        if (overrideMaterialProperties)
        {
            splatMaterial.SetFloat("_SplatScale", splatScale);
            splatMaterial.SetFloat("_MinScreenRadius", minScreenRadius);
            splatMaterial.SetFloat("_MaxScreenRadius", maxScreenRadius);
            splatMaterial.SetFloat("_ForceScreenRadius", forceScreenRadius ? 1.0f : 0.0f);
            splatMaterial.SetFloat("_ForcedScreenRadius", forcedScreenRadius);
        }

        splatMaterial.SetPass(0);

        Graphics.DrawProceduralNow(MeshTopology.Triangles, _count * 6, 1);
    }

    private bool ShouldSort(Camera cam)
    {
        if (!Application.isPlaying) return false;
        if (!sortBackToFront || _gaussians == null || _count > maxCpuSortCount) return false;
        if (Time.realtimeSinceStartup < _nextSortTime) return false;

        float cameraMoveDistance = Vector3.Distance(_lastSortCameraPosition, cam.transform.position);
        float cameraRotateAngle = Quaternion.Angle(_lastSortCameraRotation, cam.transform.rotation);
        return cameraMoveDistance > 0.01f || cameraRotateAngle > 0.1f;
    }
}
