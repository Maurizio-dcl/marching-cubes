using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace DefaultNamespace.Water
{
    public sealed class WaterDepthTextureFeature : ScriptableRendererFeature
    {
        [SerializeField] private RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        [SerializeField] private string textureName = "_WaterCameraDepthTexture";
        [SerializeField] private Shader copyDepthShader;

        private WaterDepthTexturePass _pass;

        public override void Create()
        {
            _pass?.Dispose();
            Shader shader = copyDepthShader != null
                ? copyDepthShader
                : Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
            _pass = new WaterDepthTexturePass(shader);
            _pass.renderPassEvent = renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;

            if (_pass == null || cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            {
                return;
            }

            _pass.Setup(Shader.PropertyToID(textureName));
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }

        private sealed class WaterDepthTexturePass : ScriptableRenderPass
        {
            private static readonly int CameraDepthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment");
            private readonly Material _copyDepthMaterial;
            private readonly GlobalKeyword _depthMsaa2;
            private readonly GlobalKeyword _depthMsaa4;
            private readonly GlobalKeyword _depthMsaa8;
            private readonly GlobalKeyword _outputDepth;
            private readonly Vector4 _scaleBias = new(1f, 1f, 0f, 0f);
            private RTHandle _depthCopy;
            private int _textureId;

            public WaterDepthTexturePass(Shader copyDepthShader)
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);
                _copyDepthMaterial = copyDepthShader != null
                    ? CoreUtils.CreateEngineMaterial(copyDepthShader)
                    : null;
                _depthMsaa2 = GlobalKeyword.Create(ShaderKeywordStrings.DepthMsaa2);
                _depthMsaa4 = GlobalKeyword.Create(ShaderKeywordStrings.DepthMsaa4);
                _depthMsaa8 = GlobalKeyword.Create(ShaderKeywordStrings.DepthMsaa8);
                _outputDepth = GlobalKeyword.Create(ShaderKeywordStrings._OUTPUT_DEPTH);
            }

            public void Setup(int textureId)
            {
                _textureId = textureId;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle sourceDepth = resourceData.cameraDepth;

                if (!sourceDepth.IsValid() || _copyDepthMaterial == null)
                {
                    return;
                }

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;

                RenderingUtils.ReAllocateHandleIfNeeded(
                    ref _depthCopy,
                    descriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_WaterCameraDepthTexture");
                TextureHandle destination = renderGraph.ImportTexture(_depthCopy);

                if (!destination.IsValid())
                {
                    return;
                }

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Copy Water Camera Depth",
                    out PassData passData))
                {
                    passData.source = sourceDepth;
                    passData.copyDepthMaterial = _copyDepthMaterial;
                    passData.scaleBias = _scaleBias;
                    passData.depthMsaa2 = _depthMsaa2;
                    passData.depthMsaa4 = _depthMsaa4;
                    passData.depthMsaa8 = _depthMsaa8;
                    passData.outputDepth = _outputDepth;

                    builder.UseTexture(sourceDepth, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(destination, _textureId);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        RTHandle source = data.source;
                        int samples = source.rt != null && source.rt.bindTextureMS
                            ? source.rt.antiAliasing
                            : 1;
                        context.cmd.SetKeyword(data.depthMsaa2, samples == 2);
                        context.cmd.SetKeyword(data.depthMsaa4, samples == 4);
                        context.cmd.SetKeyword(data.depthMsaa8, samples == 8);
                        context.cmd.SetKeyword(data.outputDepth, false);
                        data.copyDepthMaterial.SetTexture(CameraDepthAttachmentId, data.source);
                        Blitter.BlitTexture(context.cmd, data.source, data.scaleBias, data.copyDepthMaterial, 0);
                    });
                }
            }

            public void Dispose()
            {
                _depthCopy?.Release();
                CoreUtils.Destroy(_copyDepthMaterial);
            }

            private sealed class PassData
            {
                public TextureHandle source;
                public Material copyDepthMaterial;
                public Vector4 scaleBias;
                public GlobalKeyword depthMsaa2;
                public GlobalKeyword depthMsaa4;
                public GlobalKeyword depthMsaa8;
                public GlobalKeyword outputDepth;
            }
        }
    }
}
