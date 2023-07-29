﻿using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using PSXPrev.Common.Animator;
using PSXPrev.Common.Utils;

namespace PSXPrev.Common.Renderer
{
    public class Scene
    {
        private const float CameraMinFOV = 1f;
        private const float CameraMaxFOV = 160f; // Anything above this just looks unintelligible
        private const float CameraNearClip = 0.1f;
        private const float CameraFarClip = 500000f;
        private const float CameraMinDistance = 0.01f;
        private const float CameraMaxPitch = 89f * GeomMath.Deg2Rad; // 90f is too high, the view flips once we hit that
        private const float CameraDistanceIncrementFactor = 0.25f;
        private const float CameraPanIncrementFactor = 0.5f;

        // All of our math is based off when FOV was 60 degrees. So use 60 degrees as the base scalar.
        private static readonly float CameraBaseDistanceScalar = (float)(Math.Tan(60d * GeomMath.Deg2Rad / 2d) * 2d);

        private const float TriangleOutlineThickness = 2f;

        private const float DebugPickingRayLineThickness = 3f;
        private const float DebugPickingRayOriginSize = 0.03f;
        private static readonly Color DebugPickingRayColor = Color.Magenta;
        private static readonly Color DebugIntersectionsColor = DebugPickingRayColor;

        private const float DebugIntersectionLineThickness = 2f;

        private const float GizmoHeight = 0.075f;
        private const float GizmoWidth = 0.005f;

        private static readonly Color SelectedGizmoColor = Color.White;

        private class GizmoInfo
        {
            public Vector3 Center { get; set; }
            public Vector3 Size { get; set; }
            public Color Color { get; set; }
        }

        private static readonly Dictionary<GizmoId, GizmoInfo> GizmoInfos = new Dictionary<GizmoId, GizmoInfo>
        {
            { GizmoId.XMover, new GizmoInfo
            {
                Center = new Vector3(GizmoHeight, GizmoWidth, GizmoWidth), // X gizmo occupies the center
                Size   = new Vector3(GizmoHeight, GizmoWidth, GizmoWidth),
                Color = Color.Red,
            } },
            { GizmoId.YMover, new GizmoInfo
            {
                Center = new Vector3(GizmoWidth, -GizmoHeight + GizmoWidth, GizmoWidth), // Y center is inverted
                Size   = new Vector3(GizmoWidth, GizmoHeight - GizmoWidth, GizmoWidth),
                Color = Color.Green,
            } },
            { GizmoId.ZMover, new GizmoInfo
            {
                Center = new Vector3(GizmoWidth, GizmoWidth, GizmoHeight + GizmoWidth),
                Size   = new Vector3(GizmoWidth, GizmoWidth, GizmoHeight - GizmoWidth),
                Color = Color.Blue,
            } },
        };


        public static int AttributeIndexPosition = 0;
        public static int AttributeIndexColour = 1;
        public static int AttributeIndexNormal = 2;
        public static int AttributeIndexUv = 3;
        public static int AttributeIndexTiledArea = 4;
        public static int AttributeIndexTexture = 5;

        public static int UniformIndexMVP;
        public static int UniformIndexLightDirection;
        public static int UniformMaskColor;
        public static int UniformAmbientColor;
        public static int UniformRenderMode;
        public static int UniformTextureMode;
        public static int UniformSemiTransparentMode;
        public static int UniformLightIntensity;

        public const string AttributeNamePosition = "in_Position";
        public const string AttributeNameColour = "in_Color";
        public const string AttributeNameNormal = "in_Normal";
        public const string AttributeNameUv = "in_Uv";
        public const string AttributeNameTiledArea = "in_TiledArea";
        public const string AttributeNameTexture = "mainTex";

        public const string UniformNameMVP = "mvpMatrix";
        public const string UniformNameLightDirection = "lightDirection";
        public const string UniformMaskColorName = "maskColor";
        public const string UniformAmbientColorName = "ambientColor";
        public const string UniformRenderModeName = "renderMode";
        public const string UniformTextureModeName = "textureMode";
        public const string UniformSemiTransparentModeName = "semiTransparentMode";
        public const string UniformLightIntensityName = "lightIntensity";

        public enum GizmoId
        {
            None,
            XMover,
            YMover,
            ZMover
        }

        public bool Initialized { get; private set; }

        public MeshBatch MeshBatch { get; private set; }
        public MeshBatch GizmosMeshBatch { get; private set; }
        public LineBatch BoundsBatch { get; private set; }
        public LineBatch TriangleOutlineBatch { get; private set; }
        public LineBatch DebugIntersectionBatch { get; private set; }
        public MeshBatch DebugPickingRayOriginBatch { get; private set; }
        public LineBatch DebugPickingRayLineBatch { get; private set; }
        public AnimationBatch AnimationBatch { get; private set; }
        public TextureBinder TextureBinder { get; private set; }

        private Vector4 _transformedLight;
        private Matrix4 _projectionMatrix;
        private Matrix4 _viewMatrix; // Final view matrix.
        private Matrix4 _viewOriginMatrix; // View matrix without target translation.
        private Vector3 _viewTarget; // Center of view that camera rotates around.
        private bool _viewMatrixValid;
        private int _shaderProgram;
        private Vector3 _rayOrigin;
        private Vector3 _rayTarget;
        private Vector3 _rayDirection;
        private Vector3? _intersected;
        private List<EntityBase> _lastPickedEntities;
        private List<Tuple<ModelEntity, Triangle>> _lastPickedTriangles;
        private int _lastPickedEntityIndex;
        private int _lastPickedTriangleIndex;
        private float _cameraDistanceScalar = 1f; // Applied when using _viewMatrix(Origin) to correct distance

        // Last-stored picking ray for debug visuals
        private bool _debugRayValid;
        private Vector3 _debugRayOrigin; 
        private Vector3 _debugRayDirection;
        private Quaternion _debugRayRotation;

        private System.Drawing.Color _clearColor;
        public System.Drawing.Color ClearColor
        {
            get => _clearColor;
            set
            {
                GL.ClearColor(value.R / 255f, value.G / 255f, value.B / 255f, 0.0f);
                _clearColor = value;
            }
        }

        public bool AutoAttach { get; set; }

        public bool Wireframe { get; set; }

        public bool VibRibbonWireframe { get; set; }

        public bool ShowGizmos { get; set; } = true;

        public bool ShowBounds { get; set; } = true;

        public bool ShowDebugIntersections { get; set; } = true;

        public bool ShowDebugPickingRay { get; set; } = true;

        public bool ShowDebugVisuals { get; set; } // 3D debug information like picking ray lines

        public bool ShowVisuals { get; set; } = true; // Enables the use of ShowGizmos, ShowBounds, and ShowDebugVisuals

        public bool VerticesOnly { get; set; }

        public bool SemiTransparencyEnabled { get; set; } = true;

        public bool ForceDoubleSided { get; set; }

        public float ViewportWidth { get; private set; } = 1f;

        public float ViewportHeight { get; private set; } = 1f;

        public float CameraDistanceIncrement => CameraDistanceToTarget * CameraDistanceIncrementFactor;

        public float CameraPanIncrement => CameraDistanceToTarget * CameraPanIncrementFactor;

        private float _cameraFOV = 60f;
        public float CameraFOV
        {
            get => _cameraFOV;
            set
            {
                _cameraFOV = Math.Max(CameraMinFOV, Math.Min(CameraMaxFOV, value));
                _cameraDistanceScalar = (float)(Math.Tan(CameraFOVRads / 2d) * 2d / CameraBaseDistanceScalar);
                SetupMatrices();
                UpdateViewMatrix(); // Update view matrix because it relies on FOV to preserve distance
            }
        }
        private float CameraFOVRads => CameraFOV * GeomMath.Deg2Rad;

        private float _cameraDistance;
        public float CameraDistance
        {
            get => _cameraDistance;
            set => _cameraDistance = Math.Max(CameraMinDistance, value);
        }

        private float _cameraYaw;
        public float CameraYaw
        {
            get => _cameraYaw;
            set => _cameraYaw = GeomMath.PositiveModulus(value, (float)(Math.PI * 2));
        }

        private float _cameraPitch;
        public float CameraPitch
        {
            get => _cameraPitch;
            set => _cameraPitch = Math.Max(-CameraMaxPitch, Math.Min(CameraMaxPitch, value));
        }

        public float CameraX { get; set; }

        public float CameraY { get; set; }

        public Quaternion CameraRotation => _viewOriginMatrix.Inverted().ExtractRotation();

        public Vector3 CameraDirection => CameraRotation * Vector3.UnitZ;

        public float CameraDistanceToTarget => -_viewOriginMatrix.ExtractTranslation().Z * _cameraDistanceScalar;

        private Vector3 _lightRotation;

        public Vector3 LightRotation
        {
            get => _lightRotation;
            set
            {
                _lightRotation = value;
                UpdateLightRotation();
            }
        }

        public System.Drawing.Color MaskColor { get; set; }

        public System.Drawing.Color DiffuseColor { get; set; }
        public System.Drawing.Color AmbientColor { get; set; }

        public bool LightEnabled { get; set; } = true;

        public float LightIntensity { get; set; } = 1f;

        public decimal VertexSize
        {
            get => (decimal)GL.GetFloat(GetPName.PointSize);
            set => GL.PointSize((float)value);
        }

        public void Initialize(float width, float height)
        {
            if (Initialized)
            {
                return;
            }
            SetupGL();
            SetupShaders();
            Resize(width, height);
            SetupMatrices();
            SetupInternals();
            Initialized = true;
        }

        public void Resize(float width, float height)
        {
            ViewportWidth  = width;
            ViewportHeight = height;
            GL.Viewport(0, 0, (int)width, (int)height);
            SetupMatrices();
        }

        private void SetupInternals()
        {
            MeshBatch = new MeshBatch(this);
            GizmosMeshBatch = new MeshBatch(this);
            BoundsBatch = new LineBatch();
            TriangleOutlineBatch = new LineBatch();
            DebugIntersectionBatch = new LineBatch();
            DebugPickingRayOriginBatch = new MeshBatch(this);
            DebugPickingRayLineBatch = new LineBatch();
            AnimationBatch = new AnimationBatch(this);
            TextureBinder = new TextureBinder();
        }

        private void SetupGL()
        {
            GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            Wireframe = false;
        }

        private void SetupShaders()
        {
            var vertexShaderSource = ManifestResourceLoader.LoadTextFile("Shaders\\Shader.vert");
            var fragmentShaderSource = ManifestResourceLoader.LoadTextFile("Shaders\\Shader.frag");
            _shaderProgram = GL.CreateProgram();
            var vertexShaderAddress = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShaderAddress, vertexShaderSource);
            GL.CompileShader(vertexShaderAddress);
            GL.AttachShader(_shaderProgram, vertexShaderAddress);
            var fragmentShaderAddress = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShaderAddress, fragmentShaderSource);
            GL.CompileShader(fragmentShaderAddress);
            GL.AttachShader(_shaderProgram, fragmentShaderAddress);
            var attributes = new Dictionary<int, string>
            {
                {AttributeIndexPosition, AttributeNamePosition},
                {AttributeIndexNormal, AttributeNameNormal},
                {AttributeIndexColour, AttributeNameColour},
                {AttributeIndexUv, AttributeNameUv},
                {AttributeIndexTiledArea, AttributeNameTiledArea},
                {AttributeIndexTexture, AttributeNameTexture}
            };
            foreach (var vertexAttributeLocation in attributes)
            {
                GL.BindAttribLocation(_shaderProgram, vertexAttributeLocation.Key, vertexAttributeLocation.Value);
            }
            GL.LinkProgram(_shaderProgram);
            if (!GetLinkStatus())
            {
                throw new Exception(GetInfoLog());
            }
            UniformIndexMVP = GL.GetUniformLocation(_shaderProgram, UniformNameMVP);
            UniformIndexLightDirection = GL.GetUniformLocation(_shaderProgram, UniformNameLightDirection);
            UniformMaskColor = GL.GetUniformLocation(_shaderProgram, UniformMaskColorName);
            UniformAmbientColor = GL.GetUniformLocation(_shaderProgram, UniformAmbientColorName);
            UniformRenderMode = GL.GetUniformLocation(_shaderProgram, UniformRenderModeName);
            UniformTextureMode = GL.GetUniformLocation(_shaderProgram, UniformTextureModeName);
            UniformSemiTransparentMode = GL.GetUniformLocation(_shaderProgram, UniformSemiTransparentModeName);
            UniformLightIntensity = GL.GetUniformLocation(_shaderProgram, UniformLightIntensityName);
        }

        private bool GetLinkStatus()
        {
            int[] parameters = { 0 };
            GL.GetProgram(_shaderProgram, GetProgramParameterName.LinkStatus, parameters);
            return parameters[0] == 1;
        }

        private string GetInfoLog()
        {
            int[] infoLength = { 0 };
            GL.GetProgram(_shaderProgram, GetProgramParameterName.InfoLogLength, infoLength);
            var bufSize = infoLength[0];
            GL.GetProgramInfoLog(_shaderProgram, bufSize, out var bufferLength, out var log);
            return log;
        }

        private void SetupMatrices()
        {
            // I'm not sure what the math is here, I just know multiplying the clips like this is what works.
            // 1 FOV has too much Z-fighting, without any division and when dividing by scalar.
            var nearClip = CameraNearClip / (_cameraDistanceScalar * _cameraDistanceScalar);
            // 1 FOV will disappear too soon if we don't divide by scalar.
            // 160 FOV will disappear too soon if we divide by scalar^2.
            var farClip = CameraFarClip / _cameraDistanceScalar;

            var aspect = ViewportWidth / ViewportHeight;
            _projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(CameraFOVRads, aspect, nearClip, farClip);
        }

        private void UpdateLightRotation()
        {
            _transformedLight = GeomMath.CreateRotation(_lightRotation) * Vector4.One;
        }

        public void UpdateViewMatrix()
        {
            // The target (_viewTarget) is the origin of the view that the camera rotates around.
            // CameraY represents the vertical distance of the camera from the origin (rotated by the pitch).
            // CameraX represents the horizontal distance of the camera from the origin (rotated by the yaw).
            // The camera always rotates around the origin.
            // The camera cannot move forwards or back, it can only zoom in and out.
            var targetTranslation = Matrix4.CreateTranslation(-_viewTarget);
            var cameraTranslation = Matrix4.CreateTranslation(CameraX, -CameraY, 0f);
            var rotation = Matrix4.CreateRotationY(_cameraYaw) * Matrix4.CreateRotationX(_cameraPitch);
            var distance = -_cameraDistance / _cameraDistanceScalar;
            var eye = (rotation * new Vector4(0f, 0f, distance, 1f)).Xyz;

            // Target (0, 0, 0), then apply _viewTarget translation later, because we need _viewOriginMatrix.
            _viewOriginMatrix = Matrix4.LookAt(eye, Vector3.Zero, new Vector3(0f, -1f, 0f));
            // Apply camera translation (after rotation).
            _viewOriginMatrix *= cameraTranslation;
            // Apply target translation (before rotation).
            _viewMatrix = targetTranslation * _viewOriginMatrix;

            _viewMatrixValid = true;
        }

        public void Draw()
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Texture2D);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(_shaderProgram);

            GL.Uniform3(UniformIndexLightDirection, _transformedLight.X, _transformedLight.Y, _transformedLight.Z);
            GL.Uniform3(UniformMaskColor, MaskColor.R / 255f, MaskColor.G / 255f, MaskColor.B / 255f);
            GL.Uniform3(UniformAmbientColor, AmbientColor.R / 255f, AmbientColor.G / 255f, AmbientColor.B / 255f);
            GL.Uniform1(UniformLightIntensity, LightIntensity);
            GL.Uniform1(UniformRenderMode, LightEnabled ? 0 : 1);
            GL.Uniform1(UniformTextureMode, 0);
            GL.Uniform1(UniformSemiTransparentMode, 0);

            MeshBatch.Draw(_viewMatrix, _projectionMatrix, TextureBinder, Wireframe, true, VerticesOnly);

            // todo: we're drawing with the same depth buffer after semi-transparency,
            // so new surfaces will not render behind semi-transparent surfaces.
            GL.Uniform1(UniformRenderMode, 2);
            if (ShowVisuals && ShowBounds)
            {
                BoundsBatch.SetupAndDraw(_viewMatrix, _projectionMatrix);
            }

            // Preserve depth buffer so that it's clear where the ray is intersecting the model.
            if (ShowVisuals && ShowDebugVisuals && ShowDebugPickingRay && _debugRayValid)
            {
                // Use standard render mode for transparency, but disable lighting.
                var LightEnabledOld = LightEnabled;
                LightEnabled = false;

                DebugPickingRayLineBatch.SetupAndDraw(_viewMatrix, _projectionMatrix, DebugPickingRayLineThickness);
                // Use standard to allow for semi-transparency.
                DebugPickingRayOriginBatch.Draw(_viewMatrix, _projectionMatrix, standard: true);

                LightEnabled = LightEnabledOld;
            }

            // DebugPickingRayOriginBatch.Draw resets UniformRenderMode, so change it back.
            GL.Uniform1(UniformRenderMode, 2);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            if (ShowVisuals && ShowDebugVisuals && ShowDebugIntersections)
            {
                DebugIntersectionBatch.SetupAndDraw(_viewMatrix, _projectionMatrix, DebugIntersectionLineThickness);
            }

            GL.Clear(ClearBufferMask.DepthBufferBit);
            // todo: Should ShowBounds really determine if the selected triangle is highlighted?
            if (ShowVisuals && ShowBounds)
            {
                TriangleOutlineBatch.SetupAndDraw(_viewMatrix, _projectionMatrix, TriangleOutlineThickness);
            }

            GL.Clear(ClearBufferMask.DepthBufferBit);
            if (ShowVisuals && ShowGizmos)
            {
                GizmosMeshBatch.Draw(_viewMatrix, _projectionMatrix, standard: false);
            }

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Texture2D);
            GL.UseProgram(0);
        }

        public void ResetBatches(int meshCount)
        {
            MeshBatch.Reset(meshCount);
            GizmosMeshBatch.Reset(0);
            BoundsBatch.Reset();
            TriangleOutlineBatch.Reset();
            DebugIntersectionBatch.Reset();
            DebugPickingRayLineBatch.Reset();
            DebugPickingRayOriginBatch.Reset(1);
        }

        public void UpdateGizmos(EntityBase selectedEntityBase, GizmoId hoveredGizmo, GizmoId selectedGizmo, bool updateMeshData)
        {
            if (updateMeshData)
            {
                GizmosMeshBatch.Reset(3);
            }
            if (selectedEntityBase == null)
            {
                return;
            }
            var center = selectedEntityBase.Bounds3D.Center;
            var matrix = Matrix4.CreateTranslation(center);
            var scaleMatrix = GetGizmoScaleMatrix(center);
            var finalMatrix = scaleMatrix * matrix;

            var i = 0;
            for (var gizmo = GizmoId.XMover; gizmo <= GizmoId.ZMover; gizmo++, i++)
            {
                var gizmoInfo = GizmoInfos[gizmo];
                var selected = hoveredGizmo == gizmo || selectedGizmo == gizmo;
                var color = selected ? SelectedGizmoColor : gizmoInfo.Color;
                GizmosMeshBatch.BindCube(finalMatrix, color, gizmoInfo.Center, gizmoInfo.Size, i, null, updateMeshData);
            }

            // todo: There's probably a better place to put this, but we know
            // gizmos will be updated when necessary to preserve constant screen size.
            UpdateDebugPickingRay(updateMeshData);
        }


        public void SetDebugPickingRay(bool show = true)
        {
            if (show && !_rayDirection.IsZero())
            {
                _debugRayValid = true;
                _debugRayOrigin = _rayOrigin;
                _debugRayDirection = _rayDirection;
                _debugRayRotation = Matrix4.LookAt(_rayOrigin, _rayTarget, new Vector3(0f, -1f, 0f)).Inverted().ExtractRotation();

                UpdateDebugPickingRay(true);
            }
            else
            {
                _debugRayValid = false;

                DebugPickingRayLineBatch.Reset();
                DebugPickingRayOriginBatch.Reset(1);
            }
        }

        private void UpdateDebugPickingRay(bool updateMeshData)
        {
            if (!_debugRayValid)
            {
                return;
            }
            DebugPickingRayLineBatch.Reset();
            if (updateMeshData)
            {
                DebugPickingRayOriginBatch.Reset(1);
            }

            var farClip = CameraFarClip / _cameraDistanceScalar;
            var rayEnd = _debugRayOrigin + _debugRayDirection * farClip;
            DebugPickingRayLineBatch.AddLine(_debugRayOrigin, rayEnd, DebugPickingRayColor);
            
            var matrix = Matrix4.CreateTranslation(_debugRayOrigin);
            var scaleMatrix = GetGizmoScaleMatrix(_debugRayOrigin);
            var rotationMatrix = Matrix4.CreateFromQuaternion(_debugRayRotation);
            var finalMatrix = rotationMatrix * scaleMatrix * matrix;

            var size = new Vector3(DebugPickingRayOriginSize);
            var renderFlags = RenderFlags.SemiTransparent;
            var mixtureRate = MixtureRate.Back50_Poly50;
            DebugPickingRayOriginBatch.BindCube(finalMatrix, DebugPickingRayColor, Vector3.Zero, size, 0, null, updateMeshData, renderFlags, mixtureRate);
        }

        public EntityBase GetEntityUnderMouse(RootEntity[] checkedEntities, RootEntity selectedRootEntity, int x, int y, bool selectRoot = false)
        {
            UpdatePicking(x, y);
            var pickedEntities = new List<EntityBase>();
            if (!selectRoot)
            {
                if (checkedEntities != null)
                {
                    foreach (var entity in checkedEntities)
                    {
                        if (entity.ChildEntities != null)
                        {
                            foreach (var subEntity in entity.ChildEntities)
                            {
                                CheckEntity(subEntity, pickedEntities);
                            }
                        }
                    }
                }
                else if (selectedRootEntity != null)
                {
                    if (selectedRootEntity.ChildEntities != null)
                    {
                        foreach (var subEntity in selectedRootEntity.ChildEntities)
                        {
                            CheckEntity(subEntity, pickedEntities);
                        }
                    }
                }
            }
            pickedEntities.Sort((a, b) => a.IntersectionDistance.CompareTo(b.IntersectionDistance));
            if (!ListsMatches(pickedEntities, _lastPickedEntities))
            {
                _lastPickedEntityIndex = 0;
            }
            var pickedEntity = pickedEntities.Count > 0 ? pickedEntities[_lastPickedEntityIndex] : null;
            if (_lastPickedEntityIndex < pickedEntities.Count - 1)
            {
                _lastPickedEntityIndex++;
            }
            else
            {
                _lastPickedEntityIndex = 0;
            }
            _lastPickedEntities = pickedEntities;
            if (ShowDebugVisuals && ShowDebugIntersections)
            {
                DebugIntersectionBatch.Reset();
                foreach (var picked in pickedEntities)
                {
                    DebugIntersectionBatch.SetupEntityBounds(picked, DebugIntersectionsColor);
                }
            }
            return pickedEntity;
        }

        public Tuple<ModelEntity, Triangle> GetTriangleUnderMouse(RootEntity[] checkedEntities, RootEntity selectedRootEntity, int x, int y, bool selectRoot = false)
        {
            UpdatePicking(x, y);
            var pickedTriangles = new List<Tuple<ModelEntity, Triangle>>();
            if (!selectRoot)
            {
                if (checkedEntities != null)
                {
                    foreach (var entity in checkedEntities)
                    {
                        if (entity.ChildEntities != null)
                        {
                            foreach (var subEntity in entity.ChildEntities)
                            {
                                CheckTriangles(subEntity, pickedTriangles);
                            }
                        }
                    }
                }
                else if (selectedRootEntity != null)
                {
                    if (selectedRootEntity.ChildEntities != null)
                    {
                        foreach (var subEntity in selectedRootEntity.ChildEntities)
                        {
                            CheckTriangles(subEntity, pickedTriangles);
                        }
                    }
                }
            }
            pickedTriangles.Sort((a, b) => a.Item2.IntersectionDistance.CompareTo(b.Item2.IntersectionDistance));
            if (!ListsMatches(pickedTriangles, _lastPickedTriangles))
            {
                _lastPickedTriangleIndex = 0;
            }
            var pickedTriangle = pickedTriangles.Count > 0 ? pickedTriangles[_lastPickedTriangleIndex] : null;
            if (_lastPickedTriangleIndex < pickedTriangles.Count - 1)
            {
                _lastPickedTriangleIndex++;
            }
            else
            {
                _lastPickedTriangleIndex = 0;
            }
            _lastPickedTriangles = pickedTriangles;
            if (ShowDebugVisuals && ShowDebugIntersections)
            {
                DebugIntersectionBatch.Reset();
                foreach (var picked in pickedTriangles)
                {
                    DebugIntersectionBatch.SetupTriangleOutline(picked.Item2, picked.Item1.WorldMatrix, DebugIntersectionsColor);
                }
            }
            return pickedTriangle;
        }

        private static bool ListsMatches<T>(List<T> a, List<T> b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private void CheckEntity(EntityBase entity, List<EntityBase> pickedEntities)
        {
            GeomMath.GetBoxMinMax(entity.Bounds3D.Center, entity.Bounds3D.Extents, out var boxMin, out var boxMax);
            var intersectionDistance = GeomMath.BoxIntersect(_rayOrigin, _rayDirection, boxMin, boxMax);
            if (intersectionDistance > 0f)
            {
                entity.IntersectionDistance = intersectionDistance;
                pickedEntities.Add(entity);
            }
        }

        private void CheckTriangles(EntityBase entity, List<Tuple<ModelEntity, Triangle>> pickedTriangles)
        {
            if (entity is ModelEntity modelEntity && modelEntity.Triangles.Length > 0)
            {
                var worldMatrix = modelEntity.WorldMatrix;
                foreach (var triangle in modelEntity.Triangles)
                {
                    var vertex0 = Vector3.TransformPosition(triangle.Vertices[0], worldMatrix);
                    var vertex1 = Vector3.TransformPosition(triangle.Vertices[1], worldMatrix);
                    var vertex2 = Vector3.TransformPosition(triangle.Vertices[2], worldMatrix);

                    var intersectionDistance = GeomMath.TriangleIntersect(_rayOrigin, _rayDirection, vertex0, vertex1, vertex2);
                    if (intersectionDistance > 0f)
                    {
                        triangle.IntersectionDistance = intersectionDistance;
                        pickedTriangles.Add(new Tuple<ModelEntity, Triangle>(modelEntity, triangle));
                    }
                }
            }
        }

        public void FocusOnBounds(BoundingBox bounds)
        {
            _cameraYaw = 0f;
            _cameraPitch = 0f;
            CameraX = 0f;
            CameraY = 0f;
            // Target the center of the bounding box, so that models that aren't close to the origin are easy to view.
            _viewTarget = bounds.Center;
            DistanceToFitBounds(bounds);
        }

        public void UpdateTexture(Bitmap textureBitmap, int texturePage)
        {
            TextureBinder.UpdateTexture(textureBitmap, texturePage);
        }

        private void DistanceToFitBounds(BoundingBox bounds)
        {
            var radius = bounds.MagnitudeFromPosition(_viewTarget);
            // Legacy FOV logic: camera distance is already divided by
            // FOV distance scalar, but we want 60 FOV as the baseline.
            var distance = radius / (float)Math.Sin(60f * GeomMath.Deg2Rad / 2f) + 0.1f;
            CameraDistance = distance;
            UpdateViewMatrix();
        }

        public GizmoId GetGizmoUnderPosition(EntityBase selectedEntityBase)
        {
            if (!ShowVisuals || !ShowGizmos)
            {
                return GizmoId.None;
            }
            if (selectedEntityBase != null)
            {
                var center = selectedEntityBase.Bounds3D.Center;
                var matrix = Matrix4.CreateTranslation(center);
                var scaleMatrix = GetGizmoScaleMatrix(center);
                var finalMatrix = scaleMatrix * matrix;

                // Find the closest gizmo that's intersected
                var minIntersectionGizmo = GizmoId.None;
                var minIntersectionDistance = float.MaxValue;
                for (var gizmo = GizmoId.XMover; gizmo <= GizmoId.ZMover; gizmo++)
                {
                    var gizmoInfo = GizmoInfos[gizmo];
                    GeomMath.GetBoxMinMax(gizmoInfo.Center, gizmoInfo.Size, out var boxMin, out var boxMax, finalMatrix);

                    var intersectionDistance = GeomMath.BoxIntersect(_rayOrigin, _rayDirection, boxMin, boxMax);
                    if (intersectionDistance > 0f && intersectionDistance < minIntersectionDistance)
                    {
                        minIntersectionDistance = intersectionDistance;
                        minIntersectionGizmo = gizmo;
                    }
                }
                return minIntersectionGizmo;
            }
            return GizmoId.None;
        }

        public Vector3 GetPickedPosition(Vector3 onNormal)
        {
            return GeomMath.PlaneIntersect(_rayOrigin, _rayDirection, Vector3.Zero, onNormal);
        }

        public void UpdatePicking(int x, int y)
        {
            if (!_viewMatrixValid)
            {
                return;
            }
            // See notes in SetupMatrices.
            //var nearClip = CameraNearClip / (_cameraDistanceScalar * _cameraDistanceScalar);
            var nearClip = CameraNearClip;

            _rayOrigin = new Vector3(x, y, nearClip).UnProject(_projectionMatrix, _viewMatrix, ViewportWidth, ViewportHeight);
            _rayTarget = new Vector3(x, y, 1f).UnProject(_projectionMatrix, _viewMatrix, ViewportWidth, ViewportHeight);
            _rayDirection = (_rayTarget - _rayOrigin).Normalized();
        }

        private float CameraDistanceFrom(Vector3 point)
        {
            var distance = (_viewMatrix.Inverted().ExtractTranslation() - point).Length;
            return distance * _cameraDistanceScalar;
        }

        public Matrix4 GetGizmoScaleMatrix(Vector3 position)
        {
            return Matrix4.CreateScale(CameraDistanceFrom(position));
        }

        public void ResetIntersection()
        {
            _intersected = null;
        }
    }
}