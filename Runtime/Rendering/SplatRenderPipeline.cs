using UnityEngine;
using System.Collections.Generic;

namespace GsPhysicsToolkit.Rendering
{
    [ExecuteAlways]
    public class SplatRenderPipeline : MonoBehaviour
    {
        public bool drawInUpdate = false;
        private List<GaussianSplat> _renderers = new List<GaussianSplat>();

        void OnEnable()
        {
            RefreshRenderers();
        }

        public void RefreshRenderers()
        {
            _renderers.Clear();
            _renderers.AddRange(FindObjectsOfType<GaussianSplat>());
        }

        void Update()
        {
            if (!drawInUpdate) return;

            Camera cam = Camera.main ?? Camera.current;
            if (cam != null)
            {
                foreach (var renderer in _renderers)
                {
                    if (renderer != null) renderer.Draw(cam);
                }
            }
        }
    }
}
