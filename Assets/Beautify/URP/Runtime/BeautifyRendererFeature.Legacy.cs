using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Beautify.Universal.Beautify;

#if !UNITY_6000_4_OR_NEWER
namespace Beautify.Universal {

    public partial class BeautifyRendererFeature {

#if UNITY_2022_1_OR_NEWER
#if UNITY_6000_2_OR_NEWER
        [System.Obsolete]
#endif
        public override void SetupRenderPasses(ScriptableRenderer renderer,
                                      in RenderingData renderingData) {

            CameraData cameraData = renderingData.cameraData;
            Camera cam = cameraData.camera;
            if ((cameraLayerMask & (1 << cam.gameObject.layer)) == 0) return;

            if (cam.targetTexture != null && cam.targetTexture.format == RenderTextureFormat.Depth) return; // ignore depth pre-pass cams!

            m_BeautifyRenderPass.SetupRenderTargets(renderer);
            m_BeautifyBloomLumMaskPass.SetupRenderTargets(renderer);
            m_BeautifyAnamorphicFlaresLumMaskPass.SetupRenderTargets(renderer);
            m_BeautifySharpenExclusionMaskPass.SetupRenderTargets(renderer);
        }
#endif

        partial class BeautifyRenderPass {

#if UNITY_2022_1_OR_NEWER
            public void SetupRenderTargets(ScriptableRenderer renderer) {
                BeautifyRenderPass.renderer = renderer;
#if UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618
                source = renderer.cameraColorTargetHandle;
#pragma warning restore CS0618
#else
                source = renderer.cameraColorTarget;
#endif
            }
#else
            public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
                source = renderer.cameraColorTarget;
            }
#endif

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {

                if (bMat == null) return;

                if (beautify == null) {
                    beautify = VolumeManager.instance.stack.GetComponent<Beautify>();
                }
                if (beautify == null || !beautify.IsActive()) return;

                sourceDesc = cameraTextureDescriptor;
                sourceDesc.msaaSamples = 1;
                sourceDesc.depthBufferBits = 0;

                if (beautify.downsampling.value) {
                    UniversalRenderPipelineAsset pipe = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                    float downsamplingMultiplier = 1f / beautify.downsamplingMultiplier.value;
                    if (downsamplingMultiplier < 1f) {
                        DownsamplingMode mode = beautify.downsamplingMode.value;
                        if (mode == DownsamplingMode.BeautifyEffectsOnly) {
                            sourceDesc.width = (int)(sourceDesc.width * downsamplingMultiplier);
                            sourceDesc.height = (int)(sourceDesc.height * downsamplingMultiplier);
                            if (pipe.renderScale != 1f) {
                                pipe.renderScale = 1f;
                            }
                        }
                        else {
                            if (pipe.renderScale != downsamplingMultiplier) {
                                pipe.renderScale = downsamplingMultiplier;
                                beautify.downsamplingMultiplier.value = 1f / pipe.renderScale;
                            }
                        }
                    }
                    else {
                        if (pipe.renderScale != 1f) {
                            pipe.renderScale = 1f;
                        }
                    }
                }

                sourceDescHP = sourceDesc;
                if (supportsFPTextures) {
                    sourceDescHP.colorFormat = RenderTextureFormat.ARGBHalf;
                }

                if (!beautify.ignoreDepthTexture.value) {
                    ConfigureInput(ScriptableRenderPassInput.Depth);
                }
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
                if (bMat == null) {
                    Debug.LogError("Beautify material not initialized.");
                    return;
                }

                Camera cam = cameraData.camera;
                if (beautify == null || cam == null || !beautify.IsActive()) return;

#if !UNITY_2022_1_OR_NEWER
                if (!usesDirectWriteToCamera) {
                    source = renderer.cameraColorTarget;
                }
#endif
                var cmd = CommandBufferPool.Get("Beautify");

                passData.camera = cam;
                passData.cmd = cmd;

                ExecutePass(passData);
                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }
        }

        partial class BeautifySharpenExclusionMaskPass {

#if UNITY_2022_1_OR_NEWER
            public void SetupRenderTargets(ScriptableRenderer renderer) {
#if UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618
                depthRT = renderer.cameraDepthTargetHandle;
#pragma warning restore CS0618
#else
                depthRT = renderer.cameraDepthTarget;
#endif
            }
#else
            public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
                depthRT = renderingData.cameraData.renderer.cameraDepthTarget;
            }
#endif

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                RenderTextureDescriptor maskDesc = cameraTextureDescriptor;
                maskDesc.colorFormat = canUse16Bit ? RenderTextureFormat.RGB565 : RenderTextureFormat.ARGB32;
                maskDesc.depthBufferBits = 0;
                cmd.GetTemporaryRT(sharpenExclusionMaskId, maskDesc, FilterMode.Point);
                cmd.SetGlobalTexture(sharpenExclusionMaskRT, sharpenExclusionMaskId);
                ConfigureTarget(maskRT, depthRT);
                ConfigureClear(ClearFlag.Color, Color.black);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                SortingCriteria sortingCriteria = SortingCriteria.None;
                var drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                var filter = new FilteringSettings(RenderQueueRange.all) { layerMask = BeautifySettings.sharpenExclusionMask };
#if UNITY_2023_1_OR_NEWER
                CommandBuffer cmd = CommandBufferPool.Get("Beautify Sharpen Exclusion Mask");
                RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filter);
                RendererList list = context.CreateRendererList(ref listParams);
                cmd.DrawRendererList(list);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
#else
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filter);
#endif
            }
        }

        partial class BeautifyBloomLumMaskPass {

#if UNITY_2022_1_OR_NEWER
            public void SetupRenderTargets(ScriptableRenderer renderer) {
#if UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618
                depthRT = renderer.cameraDepthTargetHandle;
#pragma warning restore CS0618
#else
                depthRT = renderer.cameraDepthTarget;
#endif
            }
#else
            public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
                depthRT = renderingData.cameraData.renderer.cameraDepthTarget;
            }
#endif

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
                depthDesc.colorFormat = RenderTextureFormat.ARGB32;
                depthDesc.depthBufferBits = 0;
                cmd.GetTemporaryRT(bloomSourceDepthId, depthDesc, FilterMode.Point);
                cmd.SetGlobalTexture(bloomSourceDepthRT, bloomSourceDepthId);
                if (BeautifySettings.anamorphicFlaresExcludeMask == BeautifySettings.bloomExcludeMask) {
                    cmd.SetGlobalTexture(BeautifyAnamorphicFlaresLumMaskPass.afSourceDepthRT, bloomSourceDepthId);
                }
                ConfigureTarget(maskRT, depthRT);
                ConfigureClear(ClearFlag.Color, Color.black);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                SortingCriteria sortingCriteria = SortingCriteria.None;
                var drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                var filter = new FilteringSettings(RenderQueueRange.all) { layerMask = BeautifySettings.bloomExcludeMask };
#if UNITY_2023_1_OR_NEWER
                CommandBuffer cmd = CommandBufferPool.Get("Beautify Luma Mask");
                RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filter);
                RendererList list = context.CreateRendererList(ref listParams);
                cmd.DrawRendererList(list);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
#else
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filter);
#endif
            }
        }

        partial class BeautifyAnamorphicFlaresLumMaskPass {

#if UNITY_2022_1_OR_NEWER
            public void SetupRenderTargets(ScriptableRenderer renderer) {
#if UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618
                depthRT = renderer.cameraDepthTargetHandle;
#pragma warning restore CS0618
#else
                depthRT = renderer.cameraDepthTarget;
#endif
            }
#else
            public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
                depthRT = renderingData.cameraData.renderer.cameraDepthTarget;
            }
#endif

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
                depthDesc.colorFormat = RenderTextureFormat.ARGB32;
                depthDesc.depthBufferBits = 0;
                cmd.GetTemporaryRT(afSourceDepthId, depthDesc, FilterMode.Point);
                cmd.SetGlobalTexture(afSourceDepthRT, afSourceDepthId);
                ConfigureTarget(maskRT, depthRT);
                ConfigureClear(ClearFlag.Color, Color.black);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                SortingCriteria sortingCriteria = SortingCriteria.None;
                var drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                var filter = new FilteringSettings(RenderQueueRange.all) { layerMask = BeautifySettings.anamorphicFlaresExcludeMask };
#if UNITY_2023_1_OR_NEWER
                CommandBuffer cmd = CommandBufferPool.Get("AF Luma Mask");
                RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filter);
                RendererList list = context.CreateRendererList(ref listParams);
                cmd.DrawRendererList(list);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
#else
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filter);
#endif
            }
        }

        internal partial class BeautifyDoFTransparentMaskPass {

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
                depthDesc.colorFormat = RenderTextureFormat.Depth;
                depthDesc.depthBufferBits = 24;
                depthDesc.msaaSamples = 1;
                cmd.GetTemporaryRT(dofTransparentDepthId, depthDesc, FilterMode.Point);
                cmd.SetGlobalTexture(dofTransparentDepthRT, dofTransparentDepthId);
                ConfigureTarget(m_Depth);
                ConfigureClear(ClearFlag.All, Color.black);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                cmd.Clear();

                if (BeautifySettings.dofAlphaTestSupport) {
                    if (BeautifySettings.dofAlphaTestLayerMask != 0) {
                        if (BeautifySettings.dofAlphaTestLayerMask != currentAlphaCutoutLayerMask || BeautifySettings._refreshAlphaClipRenderers) {
                            FindAlphaClippingRenderers();
                        }
                        if (depthOnlyMaterialCutOff == null) {
                            Shader depthOnlyCutOff = Shader.Find(m_DepthOnlyShader);
                            depthOnlyMaterialCutOff = new Material(depthOnlyCutOff);
                        }
                        int renderersCount = cutOutRenderers.Count;
                        if (depthOverrideMaterials == null || depthOverrideMaterials.Length < renderersCount) {
                            depthOverrideMaterials = new Material[renderersCount];
                        }
                        for (int k = 0; k < renderersCount; k++) {
                            Renderer renderer = cutOutRenderers[k];
                            if (renderer != null && renderer.isVisible) {
                                Material mat = renderer.sharedMaterial;
                                if (mat != null) {
                                    if (depthOverrideMaterials[k] == null) {
                                        depthOverrideMaterials[k] = Instantiate(depthOnlyMaterialCutOff);
                                        depthOverrideMaterials[k].EnableKeyword(ShaderParams.SKW_CUSTOM_DEPTH_ALPHA_TEST);
                                    }
                                    Material overrideMaterial = depthOverrideMaterials[k];

                                    if (mat.HasProperty(ShaderParams.CustomDepthAlphaCutoff)) {
                                        overrideMaterial.SetFloat(ShaderParams.CustomDepthAlphaCutoff, mat.GetFloat(ShaderParams.CustomDepthAlphaCutoff));
                                    }
                                    else {
                                        overrideMaterial.SetFloat(ShaderParams.CustomDepthAlphaCutoff, 0.5f);
                                    }
                                    if (mat.HasProperty(ShaderParams.CustomDepthBaseMap)) {
                                        overrideMaterial.SetTexture(ShaderParams.CustomDepthBaseMap, mat.GetTexture(ShaderParams.CustomDepthBaseMap));
                                    }
                                    else if (mat.HasProperty(ShaderParams.mainTex)) {
                                        overrideMaterial.SetTexture(ShaderParams.CustomDepthBaseMap, mat.GetTexture(ShaderParams.mainTex));
                                    }
                                    overrideMaterial.SetInt(m_CullPropertyId, BeautifySettings.dofAlphaTestDoubleSided ? (int)CullMode.Off : (int)CullMode.Back);

                                    cmd.DrawRenderer(renderer, overrideMaterial);
                                }
                            }
                        }

                    }
                }

                // Render transparent objects
                if (BeautifySettings.dofTransparentSupport) {
                    if (depthOnlyMaterial == null) {
                        depthOnlyMaterial = new Material(Shader.Find(m_DepthOnlyShader));
                    }
                    depthOnlyMaterial.SetInt(m_CullPropertyId, BeautifySettings.dofTransparentDoubleSided ? (int)CullMode.Off : (int)CullMode.Back);

                    SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                    var drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                    drawingSettings.perObjectData = PerObjectData.None;
                    drawingSettings.overrideMaterial = depthOnlyMaterial;
                    var filter = new FilteringSettings(RenderQueueRange.transparent) { layerMask = BeautifySettings.dofTransparentLayerMask };
#if UNITY_2023_1_OR_NEWER
                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filter);
                    RendererList list = context.CreateRendererList(ref listParams);
                    cmd.DrawRendererList(list);
#else
                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filter);
#endif
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        partial class BeautifyOutlineDepthPrepass {

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
                depthDesc.colorFormat = BeautifySettings.outlineUseObjectId ? RenderTextureFormat.RFloat : RenderTextureFormat.Depth;
                depthDesc.depthBufferBits = 24;
                depthDesc.msaaSamples = 1;
                cmd.GetTemporaryRT(outlineDepthId, depthDesc, FilterMode.Point);
                if (BeautifySettings.outlineUseObjectId) {
                    cmd.SetGlobalTexture(outlineObjectIdRT, outlineDepthId, RenderTextureSubElement.Color);
                    cmd.SetGlobalTexture(outlineDepthRT, outlineDepthId, RenderTextureSubElement.Depth);
                }
                else {
                    cmd.SetGlobalTexture(outlineDepthRT, outlineDepthId);
                }
#if UNITY_2022_2_OR_NEWER
				ConfigureTarget(outlineDepthHandle);
#else
                RenderTargetIdentifier rti = new RenderTargetIdentifier(outlineDepthId, 0, CubemapFace.Unknown, -1);
                ConfigureTarget(rti);
#endif
                if (BeautifySettings.outlineUseObjectId) {
                    ConfigureClear(ClearFlag.Depth | ClearFlag.Color, Color.black);
                }
                else {
                    ConfigureClear(ClearFlag.Depth, Color.black);
                }
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                if (!BeautifySettings.outlineDepthPrepass) return;

                SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, sortingCriteria);
                drawingSettings.perObjectData = PerObjectData.None;

                if (BeautifySettings.outlineDepthPrepassUseOptimizedShader) {
#if UNITY_2022_3_OR_NEWER
                    if (BeautifySettings.outlineLayerCutOff > 0) {
                        Shader alphaTestShader = null;
                        if (BeautifySettings.outlineUseObjectId) {
                            if (depthOnlyShaderWithAlphaTestObjectId == null) {
                                depthOnlyShaderWithAlphaTestObjectId = Shader.Find(m_DepthOnlyWithObjectIdAlphaTestShader);
                            }
                            alphaTestShader = depthOnlyShaderWithAlphaTestObjectId;
                        } else {
                            if (depthOnlyShaderWithAlphaTest == null) {
                                depthOnlyShaderWithAlphaTest = Shader.Find(m_DepthOnlyAlphaTestShader);
                            }
                            alphaTestShader = depthOnlyShaderWithAlphaTest;
                        }
                        drawingSettings.overrideShader = alphaTestShader;
                        Shader.SetGlobalFloat(ShaderParams.CustomDepthAlphaTestCutoff, BeautifySettings.outlineLayerCutOff);
                    } else
#endif
                    {
                        Material depthMaterial = null;
                        if (BeautifySettings.outlineUseObjectId) {
                            if (depthOnlyMaterialWithObjectId == null) {
                                depthOnlyMaterialWithObjectId = new Material(Shader.Find(m_DepthOnlyWithObjectIdShader));
                            }
                            depthMaterial = depthOnlyMaterialWithObjectId;
                        }
                        else {
                            if (depthOnlyMaterial == null) {
                                depthOnlyMaterial = new Material(Shader.Find(m_DepthOnlyShader));
                            }
                            depthMaterial = depthOnlyMaterial;
                        }
                        depthMaterial.SetInt(m_CullPropertyId, (int)CullMode.Back);
                        depthMaterial.DisableKeyword(ShaderParams.SKW_CUSTOM_DEPTH_ALPHA_TEST);
                        drawingSettings.overrideMaterial = depthMaterial;
                    }
                }

                var filter = new FilteringSettings(RenderQueueRange.opaque) { layerMask = BeautifySettings.outlineLayerMask };
#if UNITY_2023_1_OR_NEWER
                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                cmd.Clear();
                RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filter);
                RendererList list = context.CreateRendererList(ref listParams);
                cmd.DrawRendererList(list);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
#else
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filter);
#endif

            }
        }

        partial class BeautifyClearColorTarget {

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        partial class BeautifyOutlineEffectPass {

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (!BeautifyRenderPass.CanExecuteOutlineBeforeTransparents) return;

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                BeautifyRenderPass.ExecuteOutlineBeforeTransparents(cmd);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
#endif
